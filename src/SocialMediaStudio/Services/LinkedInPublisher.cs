using System.Net.Http.Headers;
using System.Net.Http.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class LinkedInPublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    public LinkedInPublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "LinkedIn";
    public Task ConnectAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Use Connected Accounts to connect LinkedIn.");
    public Task DisconnectAsync(CancellationToken cancellationToken = default) { _tokens.Delete(Provider); return Task.CompletedTask; }
    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Caption)) throw new InvalidOperationException("LinkedIn posts require text.");
        if (draft.MediaFiles.Count > 0) throw new InvalidOperationException("LinkedIn media publishing is not enabled yet; remove media or publish a text-only post.");
        return Task.CompletedTask;
    }
    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        var token = _tokens.LoadOAuth(Provider) ?? throw new InvalidOperationException("LinkedIn is not connected.");
        var author = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_LINKEDIN_AUTHOR_URN");
        if (string.IsNullOrWhiteSpace(author)) throw new InvalidOperationException("LinkedIn author identity is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/posts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("LinkedIn-Version", "202601");
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        request.Content = JsonContent.Create(new { author, commentary = draft.Caption, visibility = "PUBLIC", distribution = new { feedDistribution = "MAIN_FEED", targetEntities = Array.Empty<string>(), thirdPartyDistributionChannels = Array.Empty<string>() }, lifecycleState = "PUBLISHED", isReshareDisabledByAuthor = false });
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Error(response.StatusCode));
    }
    private static string Error(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "LinkedIn authorization expired. Reconnect LinkedIn.",
        System.Net.HttpStatusCode.Forbidden => "LinkedIn refused the post. Check member posting permissions.",
        System.Net.HttpStatusCode.TooManyRequests => "LinkedIn rate limit reached. Wait before posting again.",
        _ => $"LinkedIn publish failed ({(int)status})."
    };
}