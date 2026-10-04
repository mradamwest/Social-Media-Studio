using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SocialMediaStudio.Services;

public sealed record OAuthTokenResult(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt);

public sealed class OAuthTokenExchangeService
{
    private readonly HttpClient _httpClient = new();

    public async Task<OAuthTokenResult> ExchangeAsync(
        OAuthProviderSettings settings,
        string code,
        string redirectUri,
        string? clientSecret = null,
        string? codeVerifier = null,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            [settings.ClientIdParameter] = settings.ClientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
            values["client_secret"] = clientSecret;
        if (!string.IsNullOrWhiteSpace(codeVerifier))
            values["code_verifier"] = codeVerifier;

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        if (settings.Provider.Equals("Pinterest", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientSecret))
                throw new InvalidOperationException("Pinterest requires the App secret to connect.");
            values.Remove(settings.ClientIdParameter);
            values.Remove("client_secret");
            request.Content = new FormUrlEncodedContent(values);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ClientId}:{clientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(TokenError("connection", response.StatusCode));

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        if (!root.TryGetProperty("access_token", out var accessTokenElement))
            throw new InvalidOperationException("Provider token response did not contain an access token.");

        var accessToken = accessTokenElement.GetString();
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Provider returned an empty access token.");

        string? refreshToken = null;
        if (root.TryGetProperty("refresh_token", out var refreshElement))
            refreshToken = refreshElement.GetString();

        DateTimeOffset? expiresAt = null;
        if (root.TryGetProperty("expires_in", out var expiresElement) &&
            expiresElement.TryGetInt32(out var seconds))
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);

        return new OAuthTokenResult(accessToken, refreshToken, expiresAt);
    }
    public async Task<OAuthTokenResult> RefreshAsync(
        OAuthProviderSettings settings,
        string refreshToken,
        string? clientSecret = null,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = settings.ClientId,
            ["refresh_token"] = refreshToken
        };
        if (!string.IsNullOrWhiteSpace(clientSecret))
            values["client_secret"] = clientSecret;

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        if (settings.Provider.Equals("Pinterest", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientSecret))
                throw new InvalidOperationException("Pinterest requires the App secret to refresh the connection.");
            values.Remove(settings.ClientIdParameter);
            values.Remove("client_secret");
            request.Content = new FormUrlEncodedContent(values);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ClientId}:{clientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(TokenError("token refresh", response.StatusCode));

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (!root.TryGetProperty("access_token", out var accessTokenElement) ||
            string.IsNullOrWhiteSpace(accessTokenElement.GetString()))
            throw new InvalidOperationException("Provider refresh response did not contain an access token.");

        var accessToken = accessTokenElement.GetString()!;
        var returnedRefresh = root.TryGetProperty("refresh_token", out var refreshElement)
            ? refreshElement.GetString()
            : null;
        DateTimeOffset? expiresAt = null;
        if (root.TryGetProperty("expires_in", out var expiresElement) &&
            expiresElement.TryGetInt32(out var seconds))
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);

        return new OAuthTokenResult(accessToken, returnedRefresh ?? refreshToken, expiresAt);
    }

    private static string TokenError(string operation, System.Net.HttpStatusCode status) =>
        status switch
        {
            System.Net.HttpStatusCode.BadRequest => $"Account {operation} was rejected. Check the app/client ID, redirect URI, and authorization settings.",
            System.Net.HttpStatusCode.Unauthorized => $"Account {operation} was not authorized. Check the app credentials and reconnect.",
            System.Net.HttpStatusCode.Forbidden => $"Account {operation} was refused by the provider. Check the app permissions and enabled APIs.",
            _ => $"Account {operation} failed ({(int)status})."
        };

}
