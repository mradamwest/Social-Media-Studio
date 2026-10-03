using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SocialMediaStudio.Services;

public sealed class DeveloperCredentialStore
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SocialMediaStudio", "developer-credentials");

    public void Save(string provider, string clientId, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("Provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("App / Client ID is required.", nameof(clientId));

        Directory.CreateDirectory(_directory);
        var payload = JsonSerializer.Serialize(new DeveloperCredential(
            clientId.Trim(),
            string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim()));
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(payload), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(provider), encrypted);
    }

    public DeveloperCredential? Load(string provider)
    {
        var path = PathFor(provider);
        if (!File.Exists(path)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<DeveloperCredential>(Encoding.UTF8.GetString(bytes));
        }
        catch { return null; }
    }

    private string PathFor(string provider)
    {
        var safe = string.Concat(provider.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return Path.Combine(_directory, safe + ".bin");
    }
}

public sealed record DeveloperCredential(string ClientId, string? ClientSecret);
