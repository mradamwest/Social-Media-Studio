using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SocialMediaStudio.Models;
using SocialMediaStudio.Services;

namespace SocialMediaStudio;

public partial class MainWindow : Window
{
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

    private void ConnectAccount(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string provider) return;
        MessageBox.Show(
            $"{provider} connection is ready for its OAuth/API credentials. No password will be stored by Social Media Studio.",
            $"Connect {provider}", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
