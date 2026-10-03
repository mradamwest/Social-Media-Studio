using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class ThreadsPublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    public ThreadsPublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "Threads";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use the account connection screen to connect Threads.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Caption) && draft.MediaFiles.Count == 0)
            throw new InvalidOperationException("Threads posts require text or media.");
        if (draft.MediaFiles.Count > 1)
            throw new InvalidOperationException("Threads currently supports one media item per post.");
        if (draft.MediaFiles.Count == 1 && (!Uri.TryCreate(draft.MediaFiles[0], UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Threads media currently requires a publicly reachable HTTPS URL.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        var token = _tokens.LoadOAuth(Provider)
            ?? throw new InvalidOperationException("Threads is not connected.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var createValues = new Dictionary<string,string> { ["text"] = draft.Caption };
        if (draft.MediaFiles.Count == 0) createValues["media_type"] = "TEXT";
        else
        {
            var mediaUrl = draft.MediaFiles[0];
            var path = new Uri(mediaUrl).AbsolutePath;
            var isVideo = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase);
            createValues["media_type"] = isVideo ? "VIDEO" : "IMAGE";
            createValues[isVideo ? "video_url" : "image_url"] = mediaUrl;
        }
        using var create = new FormUrlEncodedContent(createValues);
        using var created = await _http.PostAsync("https://graph.threads.net/v1.0/me/threads", create, cancellationToken);
        if (!created.IsSuccessStatusCode)
            throw new InvalidOperationException($"Threads container creation failed ({(int)created.StatusCode}).");

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken));
        var id = json.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Threads did not return a creation ID.");

        using var publish = new FormUrlEncodedContent(new Dictionary<string,string> { ["creation_id"] = id });
        using var response = await _http.PostAsync("https://graph.threads.net/v1.0/me/threads_publish", publish, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Threads publish failed ({(int)response.StatusCode}).");
    }
}
