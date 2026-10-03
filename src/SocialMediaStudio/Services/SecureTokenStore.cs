using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SocialMediaStudio.Services;

public sealed record StoredOAuthToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt);

public sealed class SecureTokenStore
{
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SocialMediaStudio", "tokens");

    public void Save(string provider, string token) =>
        SaveProtected(provider, Encoding.UTF8.GetBytes(token));

    public string? Load(string provider)
    {
        var bytes = LoadProtected(provider);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public void SaveOAuth(string provider, OAuthTokenResult token)
    {
        var payload = JsonSerializer.Serialize(new StoredOAuthToken(
            token.AccessToken, token.RefreshToken, token.ExpiresAt));
        SaveProtected(provider, Encoding.UTF8.GetBytes(payload));
    }

    public StoredOAuthToken? LoadOAuth(string provider)
    {
        var bytes = LoadProtected(provider);
        if (bytes is null) return null;
        try
        {
            return JsonSerializer.Deserialize<StoredOAuthToken>(
                Encoding.UTF8.GetString(bytes));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool IsExpired(string provider)
    {
        var token = LoadOAuth(provider);
        return token?.ExpiresAt is not null &&
               token.ExpiresAt <= DateTimeOffset.UtcNow;
    }

    public void Delete(string provider)
    {
        var path = Path.Combine(_folder, Safe(provider) + ".bin");
        if (File.Exists(path)) File.Delete(path);
    }

    private void SaveProtected(string provider, byte[] payload)
    {
        Directory.CreateDirectory(_folder);
        var bytes = ProtectedData.Protect(
            payload, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(_folder, Safe(provider) + ".bin"), bytes);
    }

    private byte[]? LoadProtected(string provider)
    {
        var path = Path.Combine(_folder, Safe(provider) + ".bin");
        if (!File.Exists(path)) return null;
        return ProtectedData.Unprotect(
            File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
    }

    private static string Safe(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit));
}
