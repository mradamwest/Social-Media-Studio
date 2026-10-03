using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class FacebookPublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    public FacebookPublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "Facebook";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use the account connection screen to connect Facebook.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Caption) && draft.MediaFiles.Count == 0)
            throw new InvalidOperationException("Facebook posts require text or media.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        if (draft.MediaFiles.Count > 0)
            throw new InvalidOperationException("Facebook media publishing is being added next.");

        var token = _tokens.LoadOAuth(Provider)
            ?? throw new InvalidOperationException("Facebook is not connected.");
        var pageId = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_FACEBOOK_PAGE_ID")
            ?? throw new InvalidOperationException("Facebook Page ID is not configured.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await _http.PostAsJsonAsync(
            $"https://graph.facebook.com/v24.0/{pageId}/feed",
            new { message = draft.Caption }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Facebook publish failed ({(int)response.StatusCode}).");
    }
}
