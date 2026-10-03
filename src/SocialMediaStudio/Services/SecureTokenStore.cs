using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SocialMediaStudio.Services;

public sealed class SecureTokenStore
{
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SocialMediaStudio", "tokens");

    public void Save(string provider, string token)
    {
        Directory.CreateDirectory(_folder);
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(_folder, Safe(provider) + ".bin"), bytes);
    }

    public string? Load(string provider)
    {
        var path = Path.Combine(_folder, Safe(provider) + ".bin");
        if (!File.Exists(path)) return null;
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(
            File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
    }

    public void Delete(string provider)
    {
        var path = Path.Combine(_folder, Safe(provider) + ".bin");
        if (File.Exists(path)) File.Delete(path);
    }

    private static string Safe(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit));
}
