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
        var pageId = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_FACEBOOK_PAGE_ID");
        if (string.IsNullOrWhiteSpace(pageId))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var pagesResponse = await _http.GetAsync("https://graph.facebook.com/v24.0/me/accounts?fields=id,name,access_token&limit=100", cancellationToken);
            if (!pagesResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Facebook Page discovery failed ({(int)pagesResponse.StatusCode}).");
            using var pagesJson = System.Text.Json.JsonDocument.Parse(await pagesResponse.Content.ReadAsStringAsync(cancellationToken));
            var pages = pagesJson.RootElement.GetProperty("data");
            if (pages.GetArrayLength() == 0)
                throw new InvalidOperationException("No Facebook Pages are available for this account.");
            var page = pages[0];
            pageId = page.GetProperty("id").GetString();
            if (page.TryGetProperty("access_token", out var pageToken) && !string.IsNullOrWhiteSpace(pageToken.GetString()))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pageToken.GetString());
        }
        if (string.IsNullOrWhiteSpace(pageId)) throw new InvalidOperationException("Facebook Page discovery did not return a Page ID.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await _http.PostAsJsonAsync(
            $"https://graph.facebook.com/v24.0/{pageId}/feed",
            new { message = draft.Caption }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Facebook publish failed ({(int)response.StatusCode}).");
    }
}
