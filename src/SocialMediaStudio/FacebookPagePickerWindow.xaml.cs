using System.Windows;

namespace SocialMediaStudio;

public sealed record FacebookPagePickerItem(string Id, string Name, string AccessToken);

public partial class FacebookPagePickerWindow : Window
{
    public FacebookPagePickerItem? SelectedPage { get; private set; }

    public FacebookPagePickerWindow(IEnumerable<FacebookPagePickerItem> pages)
    {
        InitializeComponent();
        PagesList.ItemsSource = pages;
        PagesList.SelectedIndex = 0;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (PagesList.SelectedItem is not FacebookPagePickerItem page) return;
        SelectedPage = page;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
