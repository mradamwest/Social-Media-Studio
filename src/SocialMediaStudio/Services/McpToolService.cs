using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

/// <summary>
/// MCP-facing facade. Transport code should expose only this narrow surface and never
/// SecureTokenStore or provider OAuth objects directly.
/// </summary>
public sealed class McpToolService
{
    private readonly AutomationPublishingService _automation;
    private readonly PostQueueService _queue;

    public McpToolService(AutomationPublishingService automation, PostQueueService queue)
    {
        _automation = automation;
        _queue = queue;
    }

    public Task<IReadOnlyList<PublishResult>> PublishNowAsync(
        string? caption, IReadOnlyList<string> networks, string? title = null,
        IReadOnlyList<string>? mediaFiles = null, CancellationToken cancellationToken = default)
    {
        return _automation.PublishNowAsync(
            new AutomationPostRequest(caption, networks, title, mediaFiles), cancellationToken);
    }

    public QueuedPost CreateDraft(
        string? caption, IReadOnlyList<string> networks, string? title = null,
        IReadOnlyList<string>? mediaFiles = null) =>
        _queue.Create(new AutomationPostRequest(caption, networks, title, mediaFiles));

    public QueuedPost SchedulePost(
        string? caption, IReadOnlyList<string> networks, DateTimeOffset scheduledFor,
        string? title = null, IReadOnlyList<string>? mediaFiles = null) =>
        _queue.Create(new AutomationPostRequest(caption, networks, title, mediaFiles), scheduledFor);

    public QueuedPost? GetQueuedPost(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _queue.Get(id.Trim());
    }

    public QueuedPost EditQueuedPost(
        string id, string? caption, IReadOnlyList<string> networks,
        DateTimeOffset? scheduledFor = null, string? title = null,
        IReadOnlyList<string>? mediaFiles = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Post ID is required.", nameof(id));

        return _queue.Update(
            id.Trim(),
            new AutomationPostRequest(caption, networks, title, mediaFiles),
            scheduledFor);
    }

    public IReadOnlyList<QueuedPost> ListQueuedPosts() => _queue.List();

    public bool DeleteQueuedPost(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return _queue.Delete(id.Trim());
    }

    public static IReadOnlyList<McpAccountStatus> ListConnectedAccounts(
        IEnumerable<SocialAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return accounts.Select(account => new McpAccountStatus(
            account.Provider,
            account.State.ToString(),
            string.IsNullOrWhiteSpace(account.DisplayName) ? null : account.DisplayName,
            account.TokenExpiresAt)).ToArray();
    }
}

public sealed record McpAccountStatus(
    string Provider,
    string State,
    string? DisplayName,
    DateTimeOffset? TokenExpiresAt);
