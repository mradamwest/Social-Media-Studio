using System.Windows;

namespace SocialMediaStudio;

public partial class CredentialSetupWindow : Window
{
    private readonly string _provider;
    public string ClientId => ClientIdBox.Text.Trim();
    public string? ClientSecret => string.IsNullOrWhiteSpace(ClientSecretBox.Password)
        ? null : ClientSecretBox.Password.Trim();

    public CredentialSetupWindow(string provider, string? clientId = null)
    {
        InitializeComponent();
        _provider = provider;
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

        if (!IsPlausibleClientId(_provider, ClientId))
        {
            MessageBox.Show(ClientIdGuidance(_provider), "Account Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            ClientIdBox.Focus();
            ClientIdBox.SelectAll();
            return;
        }

        DialogResult = true;
    }
    private static bool IsPlausibleClientId(string provider, string value)
    {
        if (value.Contains('@') || value.Contains(' ') || value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;
        if (provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("Instagram", StringComparison.OrdinalIgnoreCase))
            return value.All(char.IsDigit) && value.Length >= 5;
        return value.Length >= 3;
    }

    private static string ClientIdGuidance(string provider) =>
        provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase) || provider.Equals("Instagram", StringComparison.OrdinalIgnoreCase)
            ? "Enter the numeric Meta App ID from Meta for Developers — not your email address, Facebook login, or app name."
            : provider.Equals("TikTok", StringComparison.OrdinalIgnoreCase)
                ? "Enter the TikTok Client Key from your TikTok for Developers app — not your email address or TikTok username."
                : "Enter the developer App / Client ID issued by this platform — not your email address, username, or developer portal URL.";
}
