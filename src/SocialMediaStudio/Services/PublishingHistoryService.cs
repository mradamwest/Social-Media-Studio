using System.IO;
using System.Text.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class PublishingHistoryService
{
    private readonly string _path;
    private readonly string _backupPath;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly object _gate = new();

    public PublishingHistoryService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SocialMediaStudio", "publishing-history.json");
        _backupPath = _path + ".bak";
    }

    public IReadOnlyList<PublishingHistoryEntry> List()
    {
        lock (_gate) return Load().OrderByDescending(x => x.CompletedAt).ToArray();
    }

    public PublishingHistoryEntry? Get(string id)
    {
        lock (_gate) return Load().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public PublishingHistoryEntry Record(
        string source,
        PublishState state,
        IReadOnlyList<PublishResult> providerResults,
        string? queuedPostId = null,
        string? error = null)
    {
        lock (_gate)
        {
            var items = Load().ToList();
            var entry = new PublishingHistoryEntry(
                Guid.NewGuid().ToString("N"),
                source,
                queuedPostId,
                state,
                providerResults.ToArray(),
                Sanitize(error),
                DateTimeOffset.UtcNow);
            items.Add(entry);
            Save(items);
            return entry;
        }
    }

    private IReadOnlyList<PublishingHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<PublishingHistoryEntry>>(
                File.ReadAllText(_path), _json) ?? [];
        }
        catch
        {
            try
            {
                if (!File.Exists(_backupPath)) return [];
                return JsonSerializer.Deserialize<List<PublishingHistoryEntry>>(
                    File.ReadAllText(_backupPath), _json) ?? [];
            }
            catch { return []; }
        }
    }

    private void Save(IReadOnlyList<PublishingHistoryEntry> items)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var tempPath = _path + ".tmp";
        if (File.Exists(_path))
        {
            try
            {
                JsonSerializer.Deserialize<List<PublishingHistoryEntry>>(File.ReadAllText(_path), _json);
                File.Copy(_path, _backupPath, true);
            }
            catch { }
        }
        File.WriteAllText(tempPath, JsonSerializer.Serialize(items, _json));
        File.Move(tempPath, _path, true);
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("client_secret", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("authorization:", StringComparison.OrdinalIgnoreCase))
            return "Publishing failed. Sensitive provider details were removed; reconnect the account and try again.";
        return value.Length <= 300 ? value : value[..300] + "…";
    }
}

public sealed record PublishingHistoryEntry(
    string Id,
    string Source,
    string? QueuedPostId,
    PublishState State,
    IReadOnlyList<PublishResult> ProviderResults,
    string? Error,
    DateTimeOffset CompletedAt);
