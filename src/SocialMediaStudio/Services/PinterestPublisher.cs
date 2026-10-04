using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class PinterestPublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    public PinterestPublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "Pinterest";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use Connected Accounts to connect Pinterest.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Caption) && string.IsNullOrWhiteSpace(draft.Title))
            throw new InvalidOperationException("Pinterest Pins require a title or description.");
        if (draft.MediaFiles.Count > 0)
            throw new InvalidOperationException("Pinterest V1 publishing currently requires a public HTTPS media URL configured for the Pin.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        var token = _tokens.LoadOAuth(Provider) ?? throw new InvalidOperationException("Pinterest is not connected.");
        var boardId = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_PINTEREST_BOARD_ID");
        var mediaUrl = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_PINTEREST_MEDIA_URL");
        if (string.IsNullOrWhiteSpace(boardId))
            throw new InvalidOperationException("Pinterest board is not configured.");
        if (string.IsNullOrWhiteSpace(mediaUrl) || !Uri.TryCreate(mediaUrl, UriKind.Absolute, out var media) || media.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Pinterest requires a valid public HTTPS image URL.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.pinterest.com/v5/pins");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = JsonContent.Create(new
        {
            board_id = boardId,
            title = string.IsNullOrWhiteSpace(draft.Title) ? null : draft.Title,
            description = draft.Caption,
            media_source = new { source_type = "image_url", url = mediaUrl }
        });
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Error(response.StatusCode));
    }

    private static string Error(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Pinterest authorization expired. Reconnect Pinterest.",
        System.Net.HttpStatusCode.Forbidden => "Pinterest refused the Pin. Check pins:write permission and board access.",
        System.Net.HttpStatusCode.TooManyRequests => "Pinterest rate limit reached. Wait before posting again.",
        _ => $"Pinterest publish failed ({(int)status})."
    };
}
