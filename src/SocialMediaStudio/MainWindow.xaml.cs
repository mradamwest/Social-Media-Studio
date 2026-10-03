using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using SocialMediaStudio.Models;
using SocialMediaStudio.Services;

namespace SocialMediaStudio;

public partial class MainWindow : Window
{
    private readonly OAuthConnectionService _oauth = new();
    private readonly OAuthTokenExchangeService _tokenExchange = new();
    private readonly SecureTokenStore _tokens = new();
    private readonly DeveloperCredentialStore _developerCredentials = new();
    private readonly AccountStateService _accountStates;
    private readonly PostDraft _draft = new();
    private readonly PublishingCoordinator _publishing;

    public ObservableCollection<SocialAccount> Accounts { get; } =
        new(ProviderCatalog.Providers.Select(p => new SocialAccount { Provider = p }));

    public MainWindow()
    {
        _accountStates = new AccountStateService(_tokens);
        _publishing = new PublishingCoordinator(new ISocialPublisher[] { new BlueskyPublisher(_tokens), new XPublisher(_tokens), new ThreadsPublisher(_tokens), new FacebookPublisher(_tokens), new InstagramPublisher(_tokens), new YouTubePublisher(_tokens) });
        InitializeComponent();
        AccountsList.ItemsSource = Accounts;
        _accountStates.Restore(Accounts);
        AccountsList.Items.Refresh();
    }

    private void ShowCreatePost(object sender, RoutedEventArgs e)
    {
        CreatePostView.Visibility = Visibility.Visible;
        AccountsView.Visibility = Visibility.Collapsed;
    }

    private void ShowAccounts(object sender, RoutedEventArgs e)
    {
        _accountStates.Restore(Accounts);
        AccountsList.Items.Refresh();
        CreatePostView.Visibility = Visibility.Collapsed;
        AccountsView.Visibility = Visibility.Visible;
    }

    private async void ConnectAccount(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string provider) return;
        var account = Accounts.First(a => a.Provider == provider);

        if (!ProviderConnectionCatalog.Definitions.ContainsKey(provider))
        {
            MessageBox.Show($"{provider} connector will be added in the next provider batch.",
                $"Connect {provider}", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var definition = ProviderConnectionCatalog.Definitions[provider];
        var storedCredential = _developerCredentials.Load(provider);
        var clientId = storedCredential?.ClientId
            ?? Environment.GetEnvironmentVariable(definition.ClientIdSetting);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            var setup = new CredentialSetupWindow(provider) { Owner = this };
            if (setup.ShowDialog() != true) return;

            _developerCredentials.Save(provider, setup.ClientId, setup.ClientSecret);
            storedCredential = _developerCredentials.Load(provider);
            clientId = storedCredential!.ClientId;
        }

        var settings = new OAuthProviderSettings(
            definition.Provider,
            clientId,
            definition.AuthorizationEndpoint,
            definition.TokenEndpoint,
            definition.Scope);

        try
        {
            account.State = ConnectionState.Connecting;
            AccountsList.Items.Refresh();
            button.IsEnabled = false;

            var authorization = await _oauth.AuthorizeAsync(settings);
            var clientSecret = storedCredential?.ClientSecret
                ?? (string.IsNullOrWhiteSpace(definition.ClientSecretSetting)
                    ? null
                    : Environment.GetEnvironmentVariable(definition.ClientSecretSetting));
            var token = await _tokenExchange.ExchangeAsync(
                settings, authorization.Code, authorization.RedirectUri, clientSecret);

            _tokens.SaveOAuth(provider, token);
            account.State = ConnectionState.Connected;
            account.TokenExpiresAt = token.ExpiresAt;
            account.DisplayName = "Connected";
            AccountsList.Items.Refresh();

            MessageBox.Show($"{provider} is connected securely.",
                $"{provider} connected", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            account.State = ConnectionState.Error;
            AccountsList.Items.Refresh();
            MessageBox.Show(ex.Message, $"{provider} connection failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { button.IsEnabled = true; }
    }

    private static readonly HashSet<string> SupportedMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".webp", ".mp4", ".mov", ".m4v" };

    private void BrowseMedia(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Media files|*.jpg;*.jpeg;*.png;*.webp;*.mp4;*.mov;*.m4v|All files|*.*"
        };
        if (dialog.ShowDialog() == true) SetMediaFiles(dialog.FileNames);
    }

    private void MediaDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) SetMediaFiles(files);
    }

    private void SetMediaFiles(IEnumerable<string> files)
    {
        var accepted = files.Where(File.Exists)
            .Where(f => SupportedMediaExtensions.Contains(Path.GetExtension(f)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        _draft.MediaFiles.Clear();
        _draft.MediaFiles.AddRange(accepted);
        MediaDropText.Text = accepted.Count == 0
            ? "No supported media selected."
            : string.Join(Environment.NewLine, accepted.Select(Path.GetFileName));
    }

    private void PlatformSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.Tag is not string provider) return;
        if (box.IsChecked == true) _draft.Networks.Add(provider);
        else _draft.Networks.Remove(provider);
    }

    private async void PostNow(object sender, RoutedEventArgs e)
    {
        _draft.Title = TitleBox.Text.Trim();
        _draft.Caption = CaptionBox.Text.Trim();
        if (_draft.Networks.Count == 0)
        {
            MessageBox.Show("Select at least one platform.", "Post Now", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            PostNowButton.IsEnabled = false;
            _draft.State = PublishState.Publishing;
            PublishStatusText.Text = "Publishing…";

            var results = await _publishing.PublishAsync(_draft);
            var failures = results.Where(x => !x.Success).ToList();
            var successes = results.Where(x => x.Success).ToList();

            _draft.State = failures.Count == 0 ? PublishState.Published :
                successes.Count == 0 ? PublishState.Failed : PublishState.NeedsAttention;

            PublishStatusText.Text = failures.Count == 0
                ? $"Published to {successes.Count} network(s)."
                : $"{successes.Count} published, {failures.Count} need attention.";

            if (failures.Count > 0)
                MessageBox.Show(string.Join(Environment.NewLine, failures.Select(x => $"{x.Provider}: {x.Error}")),
                    "Publishing results", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _draft.State = PublishState.Failed;
            PublishStatusText.Text = "Publishing failed.";
            MessageBox.Show(ex.Message, "Publishing failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { PostNowButton.IsEnabled = true; }
    }

    private void DisconnectAccount(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string provider) return;
        var account = Accounts.First(a => a.Provider == provider);
        _accountStates.Disconnect(account);
        AccountsList.Items.Refresh();
    }
}
