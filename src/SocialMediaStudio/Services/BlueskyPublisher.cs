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

    public Task<string?> ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Caption))
            return Task.FromResult<string?>("Bluesky posts require text.");
        if (draft.Caption.Length > 300)
            return Task.FromResult<string?>("Bluesky post text exceeds 300 characters.");
        return Task.FromResult<string?>(null);
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        var token = _tokens.LoadOAuth(Provider)
            ?? throw new InvalidOperationException("Bluesky is not connected.");

        var validation = await ValidateAsync(draft, cancellationToken);
        if (validation is not null) throw new InvalidOperationException(validation);

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var did = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_BLUESKY_DID")
            ?? throw new InvalidOperationException("Bluesky DID is not configured.");

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
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Bluesky publish failed ({(int)response.StatusCode}): {body}");
        }
    }
}
