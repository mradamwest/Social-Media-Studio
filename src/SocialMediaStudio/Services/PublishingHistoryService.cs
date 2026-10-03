using System.IO;
using System.Text.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class PublishingHistoryService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public PublishingHistoryService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SocialMediaStudio", "publishing-history.json");
    }

    public IReadOnlyList<PublishingHistoryEntry> List() => Load()
        .OrderByDescending(x => x.CompletedAt).ToArray();

    public PublishingHistoryEntry? Get(string id) =>
        Load().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public PublishingHistoryEntry Record(
        string source,
        PublishState state,
        IReadOnlyList<PublishResult> providerResults,
        string? queuedPostId = null,
        string? error = null)
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
            return [];
        }
    }

    private void Save(IReadOnlyList<PublishingHistoryEntry> items)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(items, _json));
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
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
