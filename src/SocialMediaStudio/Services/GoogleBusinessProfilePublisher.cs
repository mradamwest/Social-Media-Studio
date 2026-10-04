using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class GoogleBusinessProfilePublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    public GoogleBusinessProfilePublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "Google Business Profile";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use Connected Accounts to connect Google Business Profile.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Caption))
            throw new InvalidOperationException("Google Business Profile posts require text.");
        if (draft.MediaFiles.Count > 0)
            throw new InvalidOperationException("Google Business Profile local media upload is not enabled yet; publish a text post.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        var token = _tokens.LoadOAuth(Provider) ?? throw new InvalidOperationException("Google Business Profile is not connected.");
        var accountId = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_GBP_ACCOUNT_ID");
        var locationId = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_GBP_LOCATION_ID");
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(locationId))
            throw new InvalidOperationException("Google Business Profile account and location are not configured.");

        var parent = $"accounts/{accountId}/locations/{locationId}";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://mybusiness.googleapis.com/v4/{parent}/localPosts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = JsonContent.Create(new
        {
            languageCode = "en-US",
            summary = draft.Caption,
            topicType = "STANDARD"
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Error(response.StatusCode));
    }

    private static string Error(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Google Business Profile authorization expired. Reconnect the account.",
        System.Net.HttpStatusCode.Forbidden => "Google Business Profile refused the post. Check Business Profile API access and location permissions.",
        System.Net.HttpStatusCode.NotFound => "Google Business Profile account or location was not found.",
        System.Net.HttpStatusCode.TooManyRequests => "Google Business Profile rate limit reached. Wait before posting again.",
        _ => $"Google Business Profile publish failed ({(int)status})."
    };
}
