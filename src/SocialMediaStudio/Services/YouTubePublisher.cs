using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class YouTubePublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    private readonly OAuthTokenExchangeService _tokenExchange = new();
    private readonly DeveloperCredentialStore _credentials = new();

    public YouTubePublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "YouTube";
    public event Action<long, long>? UploadProgress;

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use Connected Accounts to connect YouTube.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        var videos = draft.MediaFiles.Where(IsVideo).ToArray();
        if (videos.Length != 1)
            throw new InvalidOperationException("YouTube publishing requires exactly one video file.");
        if (!string.IsNullOrWhiteSpace(draft.Title) && draft.Title.Trim().Length > 100)
            throw new InvalidOperationException("YouTube title cannot exceed 100 characters.");
        if ((draft.Caption ?? string.Empty).Length > 5000)
            throw new InvalidOperationException("YouTube description cannot exceed 5,000 characters.");
        if (!File.Exists(videos[0]))
            throw new InvalidOperationException("The selected YouTube video file was not found.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        var token = _tokens.LoadOAuth(Provider)
            ?? throw new InvalidOperationException("YouTube is not connected.");
        if (token.ExpiresAt is not null && token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            if (string.IsNullOrWhiteSpace(token.RefreshToken))
                throw new InvalidOperationException("YouTube connection expired. Reconnect YouTube.");
            var definition = ProviderConnectionCatalog.Definitions[Provider];
            var credential = _credentials.Load(Provider);
            var clientId = credential?.ClientId ?? Environment.GetEnvironmentVariable(definition.ClientIdSetting);
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("YouTube connection needs account setup again.");
            var settings = new OAuthProviderSettings(definition.Provider, clientId, definition.AuthorizationEndpoint, definition.TokenEndpoint, definition.Scope);
            var refreshed = await _tokenExchange.RefreshAsync(settings, token.RefreshToken, credential?.ClientSecret, cancellationToken);
            _tokens.SaveOAuth(Provider, refreshed);
            token = new StoredOAuthToken(refreshed.AccessToken, refreshed.RefreshToken, refreshed.ExpiresAt);
        }

        var videoPath = draft.MediaFiles.First(IsVideo);
        var title = string.IsNullOrWhiteSpace(draft.Title)
            ? Path.GetFileNameWithoutExtension(videoPath)
            : draft.Title.Trim();
        if (title.Length > 100) title = title[..100];

        var metadata = JsonSerializer.Serialize(new
        {
            snippet = new
            {
                title,
                description = draft.Caption ?? string.Empty
            },
            status = new
            {
                privacyStatus = draft.YouTubePrivacy is "public" or "unlisted" ? draft.YouTubePrivacy : "private"
            }
        });

        using var init = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status");
        init.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        init.Headers.Add("X-Upload-Content-Type", ContentType(videoPath));
        init.Headers.Add("X-Upload-Content-Length", new FileInfo(videoPath).Length.ToString());
        init.Content = new StringContent(metadata, Encoding.UTF8, "application/json");

        using var initResponse = await _http.SendAsync(init, cancellationToken);
        if (!initResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(YouTubeError("upload initialization", initResponse.StatusCode));

        var uploadUri = initResponse.Headers.Location
            ?? throw new InvalidOperationException("YouTube did not return an upload URL.");

        await using var stream = File.OpenRead(videoPath);
        using var upload = new HttpRequestMessage(HttpMethod.Put, uploadUri);
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        upload.Content = new ProgressStreamContent(stream, ContentType(videoPath), (sent, total) => UploadProgress?.Invoke(sent, total));

        using var response = await _http.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(YouTubeError("video upload", response.StatusCode));
    }

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _source;
        private readonly Action<long, long> _progress;
        private readonly long _length;

        public ProgressStreamContent(Stream source, string contentType, Action<long, long> progress)
        {
            _source = source;
            _progress = progress;
            _length = source.Length;
            Headers.ContentType = new MediaTypeHeaderValue(contentType);
            Headers.ContentLength = _length;
        }

        protected override bool TryComputeLength(out long length) { length = _length; return true; }

        protected override async Task SerializeToStreamAsync(Stream target, System.Net.TransportContext? context)
        {
            var buffer = new byte[1024 * 1024];
            long sent = 0;
            int read;
            while ((read = await _source.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                sent += read;
                _progress(sent, _length);
            }
        }
    }

    private static string YouTubeError(string operation, System.Net.HttpStatusCode status) =>
        status switch
        {
            System.Net.HttpStatusCode.Unauthorized => $"YouTube {operation} failed because the account authorization expired. Reconnect YouTube.",
            System.Net.HttpStatusCode.Forbidden => $"YouTube {operation} was refused. Check YouTube API access, channel permissions, and upload quota.",
            System.Net.HttpStatusCode.BadRequest => $"YouTube {operation} was rejected. Check the video metadata and file.",
            _ => $"YouTube {operation} failed ({(int)status})."
        };

    private static bool IsVideo(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v";

    private static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            _ => "video/mp4"
        };
}
