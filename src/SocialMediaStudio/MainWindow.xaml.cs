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
    private readonly FacebookPageSelectionStore _facebookPageSelection = new();
    private readonly AccountStateService _accountStates;
    private readonly PostDraft _draft = new();
    private readonly PublishingCoordinator _publishing;
    private readonly PostQueueService _postQueue = new();
    private readonly MediaLibraryService _mediaLibrary= new MediaLibraryService();
    private readonly PublishingHistoryService _history = new();
    private string? _editingQueuedPostId;

    public ObservableCollection<SocialAccount> Accounts { get; } =
        new(ProviderCatalog.Providers.Select(p => new SocialAccount { Provider = p }));

    public MainWindow()
    {
        _accountStates = new AccountStateService(_tokens);
        var youtubePublisher = new YouTubePublisher(_tokens);
        youtubePublisher.UploadProgress += YouTubeUploadProgress;
        _publishing = new PublishingCoordinator(new ISocialPublisher[] { new BlueskyPublisher(_tokens), new XPublisher(_tokens), new ThreadsPublisher(_tokens), new FacebookPublisher(_tokens), new InstagramPublisher(_tokens), youtubePublisher });
        InitializeComponent();
        AccountsList.ItemsSource = Accounts;
        _accountStates.Restore(Accounts);
        AccountsList.Items.Refresh();
    }

    private void ResetComposer()
    {
        _editingQueuedPostId = null;
        TitleBox.Clear();
        CaptionBox.Clear();
        _draft.MediaFiles.Clear();
        _draft.Networks.Clear();
        _draft.ScheduledFor = null;
        _draft.State = PublishState.Draft;
        _draft.YouTubePrivacy = "private";
        if (YouTubePrivacyBox.Items.Count > 0)
            YouTubePrivacyBox.SelectedIndex = 0;
        SetPlatformChecks(Array.Empty<string>());
        MediaDropText.Text = "Drag & drop video or images here" + Environment.NewLine + "or click to browse";
        ScheduleDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        ScheduleTimeBox.Text = "6:00 PM";
        PublishStatusText.Text = "Ready for a new post.";
    }

    private void NewPost(object sender, RoutedEventArgs e)
    {
        ResetComposer();
        ShowCreatePost(sender, e);
    }

    private void ShowCreatePost(object sender, RoutedEventArgs e)
    {
        CreatePostView.Visibility = Visibility.Visible;
        AccountsView.Visibility = Visibility.Collapsed;
        PlannerView.Visibility = Visibility.Collapsed;
        MediaLibraryView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
    }

    private void RefreshAnalytics(object sender, RoutedEventArgs e)
    {
        var posts = _postQueue.List();
        _accountStates.Restore(Accounts);
        AnalyticsConnectedAccounts.Text = Accounts.Count(x => x.State == ConnectionState.Connected).ToString();
        AnalyticsTotal.Text = posts.Count.ToString();
        AnalyticsMediaPosts.Text = posts.Count(x => x.MediaFiles.Count > 0).ToString();
        var platformCounts = posts.SelectMany(x => x.Networks).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Select(g => new { Platform = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ThenBy(x => x.Platform).ToArray();
        AnalyticsPlatformSummary.Text = platformCounts.Length == 0
            ? "No platform activity yet."
            : string.Join("   •   ", platformCounts.Select(x => $"{x.Platform}: {x.Count}"));
        var recent = _history.List().Take(5).ToArray();
        AnalyticsRecentActivity.Text = recent.Length == 0
            ? "No recent activity."
            : string.Join(Environment.NewLine, recent.Select(x => $"{x.CompletedAt.ToLocalTime():MMM d, h:mm tt} — {x.Source} — {x.State}"));
        AnalyticsDrafts.Text = posts.Count(x => x.State == PublishState.Draft).ToString();
        AnalyticsPublished.Text = posts.Count(x => x.State == PublishState.Published).ToString();
        AnalyticsScheduled.Text = posts.Count(x => x.State == PublishState.Scheduled).ToString();
        AnalyticsPublishing.Text = posts.Count(x => x.State == PublishState.Publishing).ToString();
        var nextScheduled = posts.Where(x => x.State == PublishState.Scheduled && x.ScheduledFor is not null && x.ScheduledFor > DateTimeOffset.Now).OrderBy(x => x.ScheduledFor).FirstOrDefault();
        AnalyticsUpcoming.Text = nextScheduled is null
            ? "No scheduled posts."
            : $"{nextScheduled.ScheduledFor!.Value.ToLocalTime():MMM d, yyyy h:mm tt} — {(string.IsNullOrWhiteSpace(nextScheduled.Title) ? nextScheduled.Caption : nextScheduled.Title)}";
        AnalyticsFailed.Text = posts.Count(x => x.State is PublishState.Failed or PublishState.NeedsAttention).ToString();
        var completed = posts.Count(x => x.State is PublishState.Published or PublishState.Failed or PublishState.NeedsAttention);
        var published = posts.Count(x => x.State == PublishState.Published);
        AnalyticsSuccessRate.Text = completed == 0 ? "—" : $"{(int)Math.Round(published * 100d / completed)}%";
    }

    private void ShowAnalytics(object sender, RoutedEventArgs e)
    {
        RefreshAnalytics(sender, e);
        CreatePostView.Visibility = Visibility.Collapsed;
        PlannerView.Visibility = Visibility.Collapsed;
        MediaLibraryView.Visibility = Visibility.Collapsed;
        AccountsView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Visible;
    }

    private void ShowMediaLibrary(object sender, RoutedEventArgs e)
    {
        MediaLibraryList.ItemsSource = _mediaLibrary.List().Concat(_postQueue.List().SelectMany(x => x.MediaFiles)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileName).ToArray();
        CreatePostView.Visibility = Visibility.Collapsed;
        PlannerView.Visibility = Visibility.Collapsed;
        AccountsView.Visibility = Visibility.Collapsed;
        MediaLibraryView.Visibility = Visibility.Visible;
        AnalyticsView.Visibility = Visibility.Collapsed;
    }

    private void AddMediaToLibrary(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Media files|*.jpg;*.jpeg;*.png;*.webp;*.mp4;*.mov;*.m4v|All files|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        var files = dialog.FileNames.Where(File.Exists).Where(f => SupportedMediaExtensions.Contains(Path.GetExtension(f))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0)
        {
            MessageBox.Show("No supported media files were selected.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var existing = _mediaLibrary.List().Concat(_postQueue.List().SelectMany(x => x.MediaFiles)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newFiles = files.Where(x => !existing.Contains(x)).ToArray();
        if (newFiles.Length == 0)
        {
            MessageBox.Show("Selected media is already in the Media Library.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var added = _mediaLibrary.Add(newFiles);
        if (added == 0)
        {
            MessageBox.Show("No new supported media files were added.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MediaLibraryList.ItemsSource = _mediaLibrary.List().Concat(_postQueue.List().SelectMany(x => x.MediaFiles)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileName).ToArray();
        MessageBox.Show($"Added {added} media file(s) to the Media Library.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RemoveLibraryMedia(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string path) return;
        var referencedByPost = _postQueue.List().Any(x => x.MediaFiles.Contains(path, StringComparer.OrdinalIgnoreCase));
        if (referencedByPost)
        {
            MessageBox.Show("This media is still used by a saved post. Remove it from that post first.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_mediaLibrary.Remove(path))
        {
            MessageBox.Show("This item is not a standalone Media Library import.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBox.Show($"{Path.GetFileName(path)} was removed from the Media Library. The original file was not deleted.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
        MediaLibraryList.ItemsSource = _mediaLibrary.List().Concat(_postQueue.List().SelectMany(x => x.MediaFiles)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileName).ToArray();
    }

    private void UseLibraryMedia(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string path || !File.Exists(path))
        {
            MessageBox.Show("That media file is no longer available. Reopen the Media Library to refresh the list.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!SupportedMediaExtensions.Contains(Path.GetExtension(path)))
        {
            MessageBox.Show("That file type is no longer supported for posting.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_draft.MediaFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show("That media file is already attached to this post.", "Media Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var existing = _draft.MediaFiles.Concat(new[] { path });
        SetMediaFiles(existing);
        CreatePostView.Visibility = Visibility.Visible;
        PlannerView.Visibility = Visibility.Collapsed;
        MediaLibraryView.Visibility = Visibility.Collapsed;
        AccountsView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
        PublishStatusText.Text = "Media loaded from library.";
    }

    private void ShowPlanner(object sender, RoutedEventArgs e)
    {
        PlannerList.ItemsSource = _postQueue.List().OrderBy(x => x.ScheduledFor ?? x.CreatedAt).ToArray();
        CreatePostView.Visibility = Visibility.Collapsed;
        AccountsView.Visibility = Visibility.Collapsed;
        PlannerView.Visibility = Visibility.Visible;
        MediaLibraryView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
    }

    private void RefreshPlanner(object sender, RoutedEventArgs e)
    {
        PlannerList.ItemsSource = _postQueue.List().OrderBy(x => x.ScheduledFor ?? x.CreatedAt).ToArray();
    }

    private void ShowAccounts(object sender, RoutedEventArgs e)
    {
        _accountStates.Restore(Accounts);
        var savedFacebookPage = _facebookPageSelection.Load();
        var facebookAccount = Accounts.FirstOrDefault(a => a.Provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase));
        if (facebookAccount is not null && facebookAccount.State == ConnectionState.Connected && savedFacebookPage is not null)
            facebookAccount.DisplayName = savedFacebookPage.PageName;
        AccountsList.Items.Refresh();
        CreatePostView.Visibility = Visibility.Collapsed;
        AccountsView.Visibility = Visibility.Visible;
        PlannerView.Visibility = Visibility.Collapsed;
        MediaLibraryView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
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
        if (storedCredential is null && (provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase) || provider.Equals("Instagram", StringComparison.OrdinalIgnoreCase)))
        {
            var sibling = provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase) ? "Instagram" : "Facebook";
            storedCredential = _developerCredentials.Load(sibling);
            if (storedCredential is not null) _developerCredentials.Save(provider, storedCredential.ClientId, storedCredential.ClientSecret);
        }
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
            definition.Scope,
            UsePkce: definition.UsePkce,
            RequestOfflineAccess: definition.RequestOfflineAccess);

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
                settings, authorization.Code, authorization.RedirectUri, clientSecret, authorization.CodeVerifier);

            _tokens.SaveOAuth(provider, token);
            if (provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
                _facebookPageSelection.Delete();
            account.State = ConnectionState.Connected;
            account.TokenExpiresAt = token.ExpiresAt;
            account.DisplayName = "Connected";
            AccountsList.Items.Refresh();

            if (provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Facebook is connected securely. Open Connected Accounts and choose the Page you want Social Media Studio to publish to before your first post.",
                    "Facebook connected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"{provider} is connected securely.",
                    $"{provider} connected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
        if (dialog.ShowDialog() == true) SetMediaFiles(_draft.MediaFiles.Concat(dialog.FileNames));
    }

    private void MediaDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) SetMediaFiles(_draft.MediaFiles.Concat(files));
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

    private void SetPlatformChecks(IEnumerable<string> networks)
    {
        var selected = new HashSet<string>(networks, StringComparer.OrdinalIgnoreCase);
        foreach (var box in FindVisualChildren<CheckBox>(CreatePostView))
            if (box.Tag is string provider) box.IsChecked = selected.Contains(provider);
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private void PlatformSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.Tag is not string provider) return;
        if (box.IsChecked == true) _draft.Networks.Add(provider);
        else _draft.Networks.Remove(provider);
    }

    private void YouTubeUploadProgress(long sent, long total)
    {
        if (total <= 0) return;
        var percent = Math.Clamp((int)(sent * 100L / total), 0, 100);
        Dispatcher.BeginInvoke(() => PublishStatusText.Text = $"Uploading to YouTube… {percent}%");
    }

    private async void PostNow(object sender, RoutedEventArgs e)
    {
        _draft.Title = TitleBox.Text.Trim();
        _draft.Caption = CaptionBox.Text.Trim();
        if (YouTubePrivacyBox.SelectedItem is ComboBoxItem privacyItem && privacyItem.Tag is string privacy)
            _draft.YouTubePrivacy = privacy;
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

            if (_editingQueuedPostId is not null)
            {
                _postQueue.SetState(_editingQueuedPostId, _draft.State);
                if (_draft.State == PublishState.Published)
                    _editingQueuedPostId = null;
            }

            if (failures.Count == 0 && successes.Count > 0)
            {
                ResetComposer();
                PublishStatusText.Text = $"Published successfully to {successes.Count} network(s). Ready for a new post.";
            }

            if (failures.Count > 0)
                MessageBox.Show(string.Join(Environment.NewLine, failures.Select(x => $"{x.Provider}: {x.Error}")),
                    "Publishing results", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _draft.State = PublishState.Failed;
            if (_editingQueuedPostId is not null)
                _postQueue.SetState(_editingQueuedPostId, PublishState.Failed);
            PublishStatusText.Text = "Publishing failed.";
            var message = ex.Message;
            if (message.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("client_secret", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("authorization:", StringComparison.OrdinalIgnoreCase))
                message = "Publishing failed. Sensitive provider details were removed; reconnect the account and try again.";
            else if (message.Length > 300)
                message = message[..300] + "…";
            MessageBox.Show(message, "Publishing failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { PostNowButton.IsEnabled = true; }
    }

    private void SaveDraft(object sender, RoutedEventArgs e)
    {
        _draft.Title = TitleBox.Text.Trim();
        _draft.Caption = CaptionBox.Text.Trim();
        _draft.State = PublishState.Draft;
        try
        {
            var saved = _editingQueuedPostId is null
                ? _postQueue.Create(CurrentPostRequest())
                : _postQueue.Update(_editingQueuedPostId, CurrentPostRequest());
            _editingQueuedPostId = saved.Id;
            PublishStatusText.Text = $"Draft saved — {saved.Id[..8]}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Save Draft", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ScheduleDraft(object sender, RoutedEventArgs e)
    {
        _draft.Title = TitleBox.Text.Trim();
        _draft.Caption = CaptionBox.Text.Trim();
        if (_draft.Networks.Count == 0)
        {
            MessageBox.Show("Select at least one platform before scheduling.", "Schedule", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var date = ScheduleDatePicker.SelectedDate ?? DateTime.Today;
        if (!DateTime.TryParse($"{date:yyyy-MM-dd} {ScheduleTimeBox.Text}", out var localSchedule) || localSchedule <= DateTime.Now)
        {
            MessageBox.Show("Choose a valid future date and time.", "Schedule", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _draft.ScheduledFor = new DateTimeOffset(localSchedule);
        _draft.State = PublishState.Scheduled;
        try
        {
            var saved = _editingQueuedPostId is null
                ? _postQueue.Create(CurrentPostRequest(), _draft.ScheduledFor)
                : _postQueue.Update(_editingQueuedPostId, CurrentPostRequest(), _draft.ScheduledFor);
            _editingQueuedPostId = saved.Id;
            PublishStatusText.Text = $"Scheduled for {saved.ScheduledFor:MMM d, h:mm tt} — saved to Planner queue.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Schedule", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void EditPlannerPost(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id) return;
        var post = _postQueue.Get(id);
        if (post is null) return;
        if (post.State is PublishState.Publishing or PublishState.Published)
        {
            MessageBox.Show("Published or currently publishing posts are read-only. Duplicate the post to create a new editable copy.", "Planner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _editingQueuedPostId = post.Id;
        TitleBox.Text = post.Title ?? string.Empty;
        CaptionBox.Text = post.Caption;
        _draft.MediaFiles.Clear(); _draft.MediaFiles.AddRange(post.MediaFiles);
        _draft.Networks.Clear(); foreach (var network in post.Networks) _draft.Networks.Add(network);
        SetPlatformChecks(post.Networks);
        _draft.ScheduledFor = post.ScheduledFor;
        _draft.State = post.State;
        MediaDropText.Text = post.MediaFiles.Count == 0 ? "No media selected." : string.Join(Environment.NewLine, post.MediaFiles.Select(Path.GetFileName));
        CreatePostView.Visibility = Visibility.Visible;
        PlannerView.Visibility = Visibility.Collapsed;
        AccountsView.Visibility = Visibility.Collapsed;
        MediaLibraryView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
        if (post.ScheduledFor is DateTimeOffset scheduled)
        {
            var local = scheduled.ToLocalTime();
            ScheduleDatePicker.SelectedDate = local.Date;
            ScheduleTimeBox.Text = local.ToString("h:mm tt");
        }
        else
        {
            ScheduleDatePicker.SelectedDate = DateTime.Today.AddDays(1);
            ScheduleTimeBox.Text = "6:00 PM";
        }
        PublishStatusText.Text = $"Editing saved post {post.Id[..8]}";
    }

    private void DuplicatePlannerPost(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id) return;
        var post = _postQueue.Get(id);
        if (post is null) return;
        var copy = _postQueue.Create(new AutomationPostRequest(post.Caption, post.Networks, post.Title, post.MediaFiles));
        _editingQueuedPostId = copy.Id;
        TitleBox.Text = copy.Title ?? string.Empty;
        CaptionBox.Text = copy.Caption;
        _draft.MediaFiles.Clear(); _draft.MediaFiles.AddRange(copy.MediaFiles);
        _draft.Networks.Clear(); foreach (var network in copy.Networks) _draft.Networks.Add(network);
        _draft.ScheduledFor = null;
        _draft.State = PublishState.Draft;
        _draft.YouTubePrivacy = "private";
        if (YouTubePrivacyBox.Items.Count > 0)
            YouTubePrivacyBox.SelectedIndex = 0;
        SetPlatformChecks(copy.Networks);
        MediaDropText.Text = copy.MediaFiles.Count == 0 ? "No media selected." : string.Join(Environment.NewLine, copy.MediaFiles.Select(Path.GetFileName));
        ScheduleDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        ScheduleTimeBox.Text = "6:00 PM";
        ShowCreatePost(sender, e);
        PublishStatusText.Text = $"Duplicated as draft — {copy.Id[..8]}";
    }

    private void DeletePlannerPost(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id) return;
        if (MessageBox.Show("Delete this saved post?", "Planner", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _postQueue.Delete(id);
        if (string.Equals(_editingQueuedPostId, id, StringComparison.OrdinalIgnoreCase))
        {
            _editingQueuedPostId = null;
            ResetComposer();
        }
        PlannerList.ItemsSource = _postQueue.List().OrderBy(x => x.ScheduledFor ?? x.CreatedAt).ToArray();
    }

    private AutomationPostRequest CurrentPostRequest() => new(
        CaptionBox.Text.Trim(),
        _draft.Networks.ToArray(),
        string.IsNullOrWhiteSpace(TitleBox.Text) ? null : TitleBox.Text.Trim(),
        _draft.MediaFiles.ToArray());

    private void DisconnectAccount(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string provider) return;
        var account = Accounts.First(a => a.Provider == provider);
        _accountStates.Disconnect(account);
        if (provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
        {
            _facebookPageSelection.Delete();
            account.DisplayName = null;
        }
        AccountsList.Items.Refresh();
    }
    private async void ChooseFacebookPage(object sender, RoutedEventArgs e)
    {
        if (sender is Button chooseButton && chooseButton.Tag is string taggedProvider && !taggedProvider.Equals("Facebook", StringComparison.OrdinalIgnoreCase)) return;
        var token = _tokens.LoadOAuth("Facebook");
        if (token is null)
        {
            MessageBox.Show("Connect Facebook first.", "Facebook Page", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (token.ExpiresAt is not null && token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _facebookPageSelection.Delete();
            MessageBox.Show("Facebook connection has expired. Reconnect Facebook before choosing a Page.", "Facebook Page", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var response = await http.GetAsync("https://graph.facebook.com/v24.0/me/accounts?fields=id,name,access_token&limit=100");
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Facebook Page discovery failed ({(int)response.StatusCode}).");
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!json.RootElement.TryGetProperty("data", out var pageData) || pageData.ValueKind != System.Text.Json.JsonValueKind.Array)
                throw new InvalidOperationException("Facebook returned an unexpected Page list. Reconnect Facebook and try again.");
            var pages = pageData.EnumerateArray().Select(p => new
            {
                Id = p.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "Unnamed Page" : "Unnamed Page",
                Token = p.TryGetProperty("access_token", out var t) ? t.GetString() ?? "" : ""
            }).Where(p => p.Id.Length > 0 && p.Token.Length > 0).ToArray();
            if (pages.Length == 0) throw new InvalidOperationException("No Facebook Pages are available for this account.");
            var picker = new FacebookPagePickerWindow(pages.Select(p => new FacebookPagePickerItem(p.Id, p.Name, p.Token)).ToArray()) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedPage is null) return;
            _facebookPageSelection.Save(picker.SelectedPage.Id, picker.SelectedPage.Name, picker.SelectedPage.AccessToken);
            var facebookAccount = Accounts.FirstOrDefault(a => a.Provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase));
            if (facebookAccount is not null)
            {
                facebookAccount.DisplayName = picker.SelectedPage.Name;
                AccountsList.Items.Refresh();
            }
            MessageBox.Show($"Facebook will publish to {picker.SelectedPage.Name}.", "Facebook Page selected", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Facebook Page selection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
