using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class XPublisher : ISocialPublisher
{
    private readonly HttpClient _http = new();
    private readonly SecureTokenStore _tokens;
    public XPublisher(SecureTokenStore tokens) => _tokens = tokens;
    public string Provider => "X";

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Use the account connection screen to connect X.");

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _tokens.Delete(Provider);
        return Task.CompletedTask;
    }

    public Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Caption))
            throw new InvalidOperationException("X posts require text.");
        if (draft.Caption.Length > 280)
            throw new InvalidOperationException("X post text exceeds 280 characters.");
        if (draft.MediaFiles.Count > 0)
            throw new InvalidOperationException("X media upload is not enabled yet; remove media or publish a text-only post.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(draft, cancellationToken);
        var token = _tokens.LoadOAuth(Provider)
            ?? throw new InvalidOperationException("X is not connected.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await _http.PostAsJsonAsync("https://api.x.com/2/tweets",
            new { text = draft.Caption }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(XError(response.StatusCode));
    }
    private static string XError(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "X authorization expired. Reconnect the X account.",
        System.Net.HttpStatusCode.Forbidden => "X refused the post. Check app write permissions and account access.",
        System.Net.HttpStatusCode.TooManyRequests => "X rate limit reached. Wait before posting again.",
        _ => $"X publish failed ({(int)status})."
    };
}
