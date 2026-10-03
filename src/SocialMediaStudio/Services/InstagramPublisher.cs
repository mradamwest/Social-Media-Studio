using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class InstagramPublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    public InstagramPublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "Instagram";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use the account connection screen to connect Instagram.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft.MediaFiles.Count != 1)
            throw new InvalidOperationException("Instagram publishing requires exactly one image or video.");
        if (!Uri.TryCreate(draft.MediaFiles[0], UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Instagram currently requires a publicly reachable media URL. Local files must be staged before publishing.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        var token = _tokens.LoadOAuth(Provider)
            ?? throw new InvalidOperationException("Instagram is not connected.");
        var accountId = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_INSTAGRAM_ACCOUNT_ID");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            using var pagesResponse = await _http.GetAsync("https://graph.facebook.com/v24.0/me/accounts?fields=id,instagram_business_account{id,username}&limit=100", cancellationToken);
            if (!pagesResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Instagram account discovery failed ({(int)pagesResponse.StatusCode}).");
            using var pagesJson = JsonDocument.Parse(await pagesResponse.Content.ReadAsStringAsync(cancellationToken));
            foreach (var page in pagesJson.RootElement.GetProperty("data").EnumerateArray())
            {
                if (page.TryGetProperty("instagram_business_account", out var instagram) && instagram.TryGetProperty("id", out var id))
                { accountId = id.GetString(); break; }
            }
        }
        if (string.IsNullOrWhiteSpace(accountId))
            throw new InvalidOperationException("No connected Instagram professional account was found.");
        var mediaUri = new Uri(draft.MediaFiles[0]);
        var isVideo = Path.GetExtension(mediaUri.AbsolutePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                      Path.GetExtension(mediaUri.AbsolutePath).Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
                      Path.GetExtension(mediaUri.AbsolutePath).Equals(".m4v", StringComparison.OrdinalIgnoreCase);
        var createValues = new Dictionary<string,string>
        {
            [isVideo ? "video_url" : "image_url"] = draft.MediaFiles[0],
            ["caption"] = draft.Caption
        };
        if (isVideo) createValues["media_type"] = "REELS";
        using var create = new FormUrlEncodedContent(createValues);
        using var created = await _http.PostAsync($"https://graph.facebook.com/v24.0/{accountId}/media", create, cancellationToken);
        if (!created.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram media container failed ({(int)created.StatusCode}).");

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken));
        var creationId = json.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Instagram did not return a creation ID.");

        using var publish = new FormUrlEncodedContent(new Dictionary<string,string> { ["creation_id"] = creationId });
        using var response = await _http.PostAsync($"https://graph.facebook.com/v24.0/{accountId}/media_publish", publish, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram publish failed ({(int)response.StatusCode}).");
    }
}
