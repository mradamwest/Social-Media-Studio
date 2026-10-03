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
            throw new InvalidOperationException($"YouTube upload initialization failed ({(int)initResponse.StatusCode}).");

        var uploadUri = initResponse.Headers.Location
            ?? throw new InvalidOperationException("YouTube did not return an upload URL.");

        await using var stream = File.OpenRead(videoPath);
        using var upload = new HttpRequestMessage(HttpMethod.Put, uploadUri);
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        upload.Content = new StreamContent(stream);
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType(videoPath));
        upload.Content.Headers.ContentLength = stream.Length;

        using var response = await _http.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"YouTube video upload failed ({(int)response.StatusCode}).");
    }

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
