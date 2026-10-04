using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class BlueskyPublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;

    public BlueskyPublisher(SecureTokenStore tokens) => _tokens = tokens;

    public string Provider => "Bluesky";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Bluesky connection uses its dedicated AT Protocol authorization flow.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Caption))
            throw new InvalidOperationException("Bluesky posts require text.");
        if (draft.Caption.Length > 300)
            throw new InvalidOperationException("Bluesky post text exceeds 300 characters.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        var token = _tokens.LoadOAuth(Provider)
            ?? throw new InvalidOperationException("Bluesky is not connected.");

        await ValidateAsync(draft, cancellationToken);

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var profile = _tokens.LoadBlueskyProfile();
        var did = profile?.Did;
        if (string.IsNullOrWhiteSpace(did))
            throw new InvalidOperationException("Bluesky account profile is missing. Reconnect Bluesky.");

        var payload = new
        {
            repo = did,
            collection = "app.bsky.feed.post",
            record = new
            {
                text = draft.Caption,
                createdAt = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        using var response = await _http.PostAsJsonAsync(
            "https://bsky.social/xrpc/com.atproto.repo.createRecord",
            payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BlueskyError(response.StatusCode));
        }
    }

    private static string BlueskyError(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Bluesky authorization expired. Reconnect the Bluesky account.",
        System.Net.HttpStatusCode.Forbidden => "Bluesky refused the post. Check account and app permissions.",
        System.Net.HttpStatusCode.TooManyRequests => "Bluesky rate limit reached. Wait before posting again.",
        _ => $"Bluesky publish failed ({(int)status})."
    };
}
