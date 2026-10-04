using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class FacebookPublisher : ISocialPublisher
{
    private sealed record FacebookPage(string Id, string Name, string? AccessToken);
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    private readonly FacebookPageSelectionStore _pageSelection = new();
    public FacebookPublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "Facebook";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use the account connection screen to connect Facebook.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        _pageSelection.Delete();
        _http.DefaultRequestHeaders.Authorization = null;
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
        if (draft.MediaFiles.Count > 1)
            throw new InvalidOperationException("Facebook currently supports one media file per post.");

        var token = _tokens.LoadOAuth(Provider)
            ?? throw new InvalidOperationException("Facebook is not connected.");
        var selectedPage = _pageSelection.Load();
        if (selectedPage is not null && (string.IsNullOrWhiteSpace(selectedPage.PageId) || string.IsNullOrWhiteSpace(selectedPage.AccessToken)))
        {
            _pageSelection.Delete();
            selectedPage = null;
        }
        var pageId = selectedPage?.PageId;
        var effectivePageToken = selectedPage?.AccessToken;
        if (!string.IsNullOrWhiteSpace(effectivePageToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", effectivePageToken);
        if (string.IsNullOrWhiteSpace(pageId))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var pagesResponse = await _http.GetAsync("https://graph.facebook.com/v24.0/me/accounts?fields=id,name,access_token&limit=100", cancellationToken);
            if (!pagesResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Facebook Page discovery failed ({(int)pagesResponse.StatusCode}).");
            using var pagesJson = System.Text.Json.JsonDocument.Parse(await pagesResponse.Content.ReadAsStringAsync(cancellationToken));
            var pages = pagesJson.RootElement.GetProperty("data").EnumerateArray()
                .Select(page => new FacebookPage(
                    page.GetProperty("id").GetString() ?? string.Empty,
                    page.TryGetProperty("name", out var name) ? name.GetString() ?? "Unnamed Page" : "Unnamed Page",
                    page.TryGetProperty("access_token", out var pageToken) ? pageToken.GetString() : null))
                .Where(page => !string.IsNullOrWhiteSpace(page.Id))
                .ToList();
            if (pages.Count == 0)
                throw new InvalidOperationException("No Facebook Pages are available for this account.");
            if (pages.Count > 1)
                throw new InvalidOperationException($"Multiple Facebook Pages are available ({string.Join(", ", pages.Select(page => page.Name))}). Select a Page in Connected Accounts before publishing.");
            var page = pages[0];
            pageId = page.Id;
            if (!string.IsNullOrWhiteSpace(page.AccessToken))
            {
                effectivePageToken = page.AccessToken;
                _pageSelection.Save(page.Id, page.Name, effectivePageToken);
                selectedPage = new FacebookPageSelection(page.Id, page.Name, effectivePageToken);
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", effectivePageToken);
            }
        }
        if (string.IsNullOrWhiteSpace(pageId)) throw new InvalidOperationException("Facebook Page discovery did not return a Page ID.");

        if (string.IsNullOrWhiteSpace(effectivePageToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        HttpResponseMessage response;
        if (draft.MediaFiles.Count == 0)
        {
            response = await _http.PostAsJsonAsync($"https://graph.facebook.com/v24.0/{pageId}/feed", new { message = draft.Caption }, cancellationToken);
        }
        else
        {
            var mediaPath = draft.MediaFiles[0];
            if (!File.Exists(mediaPath)) throw new InvalidOperationException("Facebook media file was not found.");
            var extension = Path.GetExtension(mediaPath).ToLowerInvariant();
            var isVideo = extension is ".mp4" or ".mov" or ".m4v";
            using var form = new MultipartFormDataContent();
            await using var stream = File.OpenRead(mediaPath);
            using var media = new StreamContent(stream);
            media.Headers.ContentType = new MediaTypeHeaderValue(isVideo ? "video/mp4" : extension == ".png" ? "image/png" : "image/jpeg");
            form.Add(media, isVideo ? "source" : "source", Path.GetFileName(mediaPath));
            form.Add(new StringContent(draft.Caption ?? string.Empty), isVideo ? "description" : "caption");
            response = await _http.PostAsync($"https://graph.facebook.com/v24.0/{pageId}/{(isVideo ? "videos" : "photos")}", form, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            if (selectedPage is not null && response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                _pageSelection.Delete();
                throw new InvalidOperationException("Facebook Page access expired or was revoked. Open Connected Accounts and choose the Page again.");
            }
            throw new InvalidOperationException($"Facebook publish failed ({(int)response.StatusCode}).");
        }
    }
}
