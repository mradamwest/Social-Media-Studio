using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

/// <summary>
/// Stable application-facing contract for trusted automation clients such as an MCP server.
/// Keeps external automation on the same publishing path as the desktop UI.
/// </summary>
public sealed class AutomationPublishingService
{
    private readonly PublishingCoordinator _publishing;

    public AutomationPublishingService(PublishingCoordinator publishing)
    {
        _publishing = publishing;
    }

    public async Task<IReadOnlyList<PublishResult>> PublishNowAsync(
        AutomationPostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Networks is null || request.Networks.Count == 0)
            throw new ArgumentException("At least one social network must be selected.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Caption) &&
            string.IsNullOrWhiteSpace(request.Title) &&
            (request.MediaFiles is null || request.MediaFiles.Count == 0))
            throw new ArgumentException("A post must contain text, a title, or media.", nameof(request));

        var draft = new PostDraft
        {
            Caption = request.Caption?.Trim() ?? string.Empty,
            Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
            State = PublishState.Publishing
        };

        foreach (var network in request.Networks.Where(n => !string.IsNullOrWhiteSpace(n)))
            draft.Networks.Add(network.Trim());

        if (request.MediaFiles is not null)
        {
            foreach (var mediaFile in request.MediaFiles.Where(f => !string.IsNullOrWhiteSpace(f)))
                draft.MediaFiles.Add(mediaFile.Trim());
        }

        return await _publishing.PublishAsync(draft, cancellationToken);
    }
}

public sealed record AutomationPostRequest(
    string? Caption,
    IReadOnlyList<string> Networks,
    string? Title = null,
    IReadOnlyList<string>? MediaFiles = null);
