using System.Windows;

namespace SocialMediaStudio;

public partial class CredentialSetupWindow : Window
{
    public string ClientId => ClientIdBox.Text.Trim();
    public string? ClientSecret => string.IsNullOrWhiteSpace(ClientSecretBox.Password)
        ? null : ClientSecretBox.Password.Trim();

    public CredentialSetupWindow(string provider, string? clientId = null)
    {
        InitializeComponent();
        HeadingText.Text = $"Set up {provider}";
        ClientIdBox.Text = clientId ?? string.Empty;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            MessageBox.Show("Enter the App / Client ID.", "Account Setup",
                MessageBoxButton.OK, MessageBoxImage.Information);
            ClientIdBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
