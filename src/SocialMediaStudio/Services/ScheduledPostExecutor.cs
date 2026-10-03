using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

/// <summary>
/// Executes due scheduled posts through the same validated publishing path used by the UI and MCP.
/// </summary>
public sealed class ScheduledPostExecutor
{
    private readonly PostQueueService _queue;
    private readonly AutomationPublishingService _automation;

    public ScheduledPostExecutor(PostQueueService queue, AutomationPublishingService automation)
    {
        _queue = queue;
        _automation = automation;
    }

    public void ResetInterruptedItems()\n    {\n        foreach (var item in _queue.List().Where(x => x.State == PublishState.Publishing))\n            _queue.SetState(item.Id, PublishState.Scheduled);\n    }\n\n    public async Task<IReadOnlyList<ScheduledExecutionResult>> ExecuteDueAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        var due = _queue.List()
            .Where(x => x.State == PublishState.Scheduled &&
                        x.ScheduledFor is not null &&
                        x.ScheduledFor <= clock)
            .OrderBy(x => x.ScheduledFor)
            .ToArray();

        var results = new List<ScheduledExecutionResult>();

        foreach (var item in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _queue.SetState(item.Id, PublishState.Publishing);
                var request = new AutomationPostRequest(
                    item.Caption, item.Networks, item.Title, item.MediaFiles);

                var publishResults = await _automation.PublishNowAsync(request, cancellationToken);
                var failures = publishResults.Where(x => !x.Success).ToArray();
                var state = failures.Length == 0
                    ? PublishState.Published
                    : failures.Length == publishResults.Count
                        ? PublishState.Failed
                        : PublishState.NeedsAttention;

                _queue.SetState(item.Id, state);
                results.Add(new ScheduledExecutionResult(item.Id, state, publishResults));
            }
            catch (Exception ex)
            {
                _queue.SetState(item.Id, PublishState.Failed);
                results.Add(new ScheduledExecutionResult(
                    item.Id,
                    PublishState.Failed,
                    [],
                    Sanitize(ex.Message)));
            }
        }

        return results;
    }

    private static string Sanitize(string message) =>
        string.IsNullOrWhiteSpace(message)
            ? "Scheduled publishing failed."
            : message.Length <= 300 ? message : message[..300] + "…";
}

public sealed record ScheduledExecutionResult(
    string PostId,
    PublishState State,
    IReadOnlyList<PublishResult> ProviderResults,
    string? Error = null);
