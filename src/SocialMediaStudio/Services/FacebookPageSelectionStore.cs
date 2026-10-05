using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace SocialMediaStudio.Services;

public sealed record FacebookPageSelection(string PageId, string PageName, string AccessToken);

public sealed class FacebookPageSelectionStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SocialMediaStudio", "facebook-page.bin");

    public void Save(string pageId, string pageName, string accessToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(new FacebookPageSelection(pageId, pageName, accessToken));
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public FacebookPageSelection? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<FacebookPageSelection>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            Delete();
            return null;
        }
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
