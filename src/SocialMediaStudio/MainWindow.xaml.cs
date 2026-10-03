using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SocialMediaStudio.Models;
using SocialMediaStudio.Services;

namespace SocialMediaStudio;

public partial class MainWindow : Window
{
    private readonly OAuthConnectionService _oauth = new();

    public ObservableCollection<SocialAccount> Accounts { get; } =
        new(ProviderCatalog.Providers.Select(p => new SocialAccount { Provider = p }));

    public MainWindow()
    {
        InitializeComponent();
        AccountsList.ItemsSource = Accounts;
    }

    private void ShowCreatePost(object sender, RoutedEventArgs e)
    {
        CreatePostView.Visibility = Visibility.Visible;
        AccountsView.Visibility = Visibility.Collapsed;
    }

    private void ShowAccounts(object sender, RoutedEventArgs e)
    {
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

        if (!ProviderConnectionCatalog.TryCreateSettings(provider, out var settings) || settings is null)
        {
            var definition = ProviderConnectionCatalog.Definitions[provider];
            MessageBox.Show(
                $"Set {definition.ClientIdSetting} with your developer app ID before connecting {provider}. " +
                "Secrets are never hardcoded in Social Media Studio.",
                $"{provider} setup required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            account.State = ConnectionState.Connecting;
            AccountsList.Items.Refresh();
            button.IsEnabled = false;

            await _oauth.AuthorizeAsync(settings);
            account.State = ConnectionState.Connected;
            account.DisplayName = "Authorization received";
            AccountsList.Items.Refresh();

            MessageBox.Show(
                $"{provider} authorization completed. Token exchange is the next connection step.",
                $"{provider} connected", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            account.State = ConnectionState.Error;
            AccountsList.Items.Refresh();
            MessageBox.Show(ex.Message, $"{provider} connection failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
