using System.Windows;
namespace SocialMediaStudio;
public partial class BlueskySetupWindow : Window
{
 public string Handle => HandleBox.Text.Trim();
 public string AppPassword => PasswordBox.Password.Trim();
 public BlueskySetupWindow() => InitializeComponent();
 private void ConnectClick(object sender, RoutedEventArgs e)
 {
  if (string.IsNullOrWhiteSpace(Handle) || string.IsNullOrWhiteSpace(AppPassword))
  { MessageBox.Show("Enter your Bluesky handle and an App Password.", "Connect Bluesky", MessageBoxButton.OK, MessageBoxImage.Information); return; }
  DialogResult = true;
 }
}