using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed record PublishResult(string Provider, bool Success, string? Error = null);

public sealed class PublishingCoordinator
{
    private readonly IReadOnlyDictionary<string, ISocialPublisher> _publishers;

    public PublishingCoordinator(IEnumerable<ISocialPublisher> publishers)
    {
        _publishers = publishers.ToDictionary(p => p.Provider, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<PublishResult>> PublishAsync(
        PostDraft draft,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PublishResult>();

        foreach (var provider in draft.Networks.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_publishers.TryGetValue(provider, out var publisher))
            {
                results.Add(new PublishResult(provider, false, "Publishing adapter is not available yet."));
                continue;
            }

            try
            {
                await publisher.ValidateAsync(draft, cancellationToken);
                await publisher.PublishAsync(draft, cancellationToken);
                results.Add(new PublishResult(provider, true));
            }
            catch (Exception ex)
            {
                results.Add(new PublishResult(provider, false, Sanitize(ex.Message)));
            }
        }

        return results;
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Publishing failed.";
        return message.Length <= 300 ? message : message[..300] + "…";
    }
}
