using System.Text.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

/// <summary>
/// Local persistence for drafts and scheduled posts created by the desktop UI or MCP.
/// This store contains post content only; credentials remain exclusively in SecureTokenStore.
/// </summary>
public sealed class PostQueueService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public PostQueueService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SocialMediaStudio", "post-queue.json");
    }

    public IReadOnlyList<QueuedPost> List() => Load();

    public QueuedPost Create(AutomationPostRequest request, DateTimeOffset? scheduledFor = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request, scheduledFor);

        var items = Load().ToList();
        var item = new QueuedPost(
            Guid.NewGuid().ToString("N"),
            request.Caption?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
            request.Networks.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            request.MediaFiles?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? [],
            scheduledFor,
            scheduledFor is null ? PublishState.Draft : PublishState.Scheduled,
            DateTimeOffset.UtcNow);

        items.Add(item);
        Save(items);
        return item;
    }

    public bool Delete(string id)
    {
        var items = Load().ToList();
        var removed = items.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Save(items);
        return removed;
    }

    private static void Validate(AutomationPostRequest request, DateTimeOffset? scheduledFor)
    {
        if (request.Networks is null || request.Networks.Count == 0)
            throw new ArgumentException("At least one social network must be selected.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Caption) &&
            string.IsNullOrWhiteSpace(request.Title) &&
            (request.MediaFiles is null || request.MediaFiles.Count == 0))
            throw new ArgumentException("A post must contain text, a title, or media.", nameof(request));

        if (scheduledFor is not null && scheduledFor <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Scheduled time must be in the future.", nameof(scheduledFor));
    }

    private IReadOnlyList<QueuedPost> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<QueuedPost>>(File.ReadAllText(_path), _json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save(IReadOnlyList<QueuedPost> items)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(items, _json));
    }
}

public sealed record QueuedPost(
    string Id,
    string Caption,
    string? Title,
    IReadOnlyList<string> Networks,
    IReadOnlyList<string> MediaFiles,
    DateTimeOffset? ScheduledFor,
    PublishState State,
    DateTimeOffset CreatedAt);
