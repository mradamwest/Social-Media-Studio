using System.IO;
using System.Text.Json;

namespace SocialMediaStudio.Services;

public sealed class MediaLibraryService
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly string _backupPath;

    public MediaLibraryService(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SocialMediaStudio", "media-library.json");
        _backupPath = _path + ".bak";
    }

    public IReadOnlyList<string> List()
    {
        lock (_gate) return Load();
    }

    public bool Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        lock (_gate)
        {
            var items = Load().ToList();
            var removed = items.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items.OrderBy(Path.GetFileName), new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(_path))
            {
                try { _ = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path)); File.Copy(_path, _backupPath, true); }
                catch { }
            }
            File.Move(temp, _path, true);
            return true;
        }
    }

    public int Add(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            var items = Load().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var before = items.Count;
            foreach (var path in paths.Where(File.Exists))
            {
                try { items.Add(Path.GetFullPath(path)); }
                catch { }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items.OrderBy(Path.GetFileName), new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(_path))
            {
                try { _ = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path)); File.Copy(_path, _backupPath, true); }
                catch { }
            }
            File.Move(temp, _path, true);
            return items.Count - before;
        }
    }

    private string[] Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path)) ?? [])
                .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch
        {
            try
            {
                var corruptPath = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                File.Move(_path, corruptPath, false);
            }
            catch { }
            try
            {
                if (File.Exists(_backupPath))
                    return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(_backupPath)) ?? []).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch { }
            return [];
        }
    }
}
