using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class TikTokPublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    public TikTokPublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "TikTok";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use Connected Accounts to connect TikTok.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft.MediaFiles.Count != 1)
            throw new InvalidOperationException("TikTok Direct Post currently requires exactly one local video.");
        var path = draft.MediaFiles[0];
        if (!File.Exists(path))
            throw new InvalidOperationException("TikTok video file could not be found.");
        var ext = Path.GetExtension(path);
        if (!ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".mov", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TikTok video must be MP4, MOV, or WebM.");
        if (draft.Caption.Length > 2200)
            throw new InvalidOperationException("TikTok caption exceeds 2200 characters.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        var token = _tokens.LoadOAuth(Provider) ?? throw new InvalidOperationException("TikTok is not connected.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using var creatorResponse = await _http.PostAsJsonAsync(
            "https://open.tiktokapis.com/v2/post/publish/creator_info/query/", new { }, cancellationToken);
        if (!creatorResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(Error(creatorResponse.StatusCode));

        using var creatorDoc = JsonDocument.Parse(await creatorResponse.Content.ReadAsStringAsync(cancellationToken));
        var data = creatorDoc.RootElement.GetProperty("data");
        var privacy = data.GetProperty("privacy_level_options").EnumerateArray()
            .Select(x => x.GetString()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? throw new InvalidOperationException("TikTok did not return an available privacy option.");
        var disableComment = data.TryGetProperty("comment_disabled", out var cd) && cd.GetBoolean();
        var disableDuet = data.TryGetProperty("duet_disabled", out var dd) && dd.GetBoolean();
        var disableStitch = data.TryGetProperty("stitch_disabled", out var sd) && sd.GetBoolean();

        var path = draft.MediaFiles[0];
        var size = new FileInfo(path).Length;
        var chunkSize = size;
        var payload = new
        {
            post_info = new
            {
                title = draft.Caption,
                privacy_level = privacy,
                disable_duet = disableDuet,
                disable_comment = disableComment,
                disable_stitch = disableStitch,
                brand_content_toggle = false,
                brand_organic_toggle = false
            },
            source_info = new { source = "FILE_UPLOAD", video_size = size, chunk_size = chunkSize, total_chunk_count = 1 }
        };

        using var initResponse = await _http.PostAsJsonAsync(
            "https://open.tiktokapis.com/v2/post/publish/video/init/", payload, cancellationToken);
        if (!initResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(Error(initResponse.StatusCode));

        using var initDoc = JsonDocument.Parse(await initResponse.Content.ReadAsStringAsync(cancellationToken));
        var uploadUrl = initDoc.RootElement.GetProperty("data").GetProperty("upload_url").GetString();
        if (string.IsNullOrWhiteSpace(uploadUrl))
            throw new InvalidOperationException("TikTok did not provide a video upload URL.");

        await using var stream = File.OpenRead(path);
        using var body = new StreamContent(stream);
        body.Headers.ContentType = new MediaTypeHeaderValue(ContentType(path));
        body.Headers.ContentLength = size;
        body.Headers.TryAddWithoutValidation("Content-Range", $"bytes 0-{size - 1}/{size}");
        using var upload = await _http.PutAsync(uploadUrl, body, cancellationToken);
        if (!upload.IsSuccessStatusCode)
            throw new InvalidOperationException($"TikTok video upload failed ({(int)upload.StatusCode}).");
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        _ => "video/mp4"
    };

    private static string Error(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "TikTok authorization expired. Reconnect TikTok.",
        System.Net.HttpStatusCode.Forbidden => "TikTok refused the post. Check Content Posting API approval and video.publish permission.",
        System.Net.HttpStatusCode.TooManyRequests => "TikTok rate limit reached. Wait before posting again.",
        _ => $"TikTok publish failed ({(int)status})."
    };
}
