using System.Net.Http;
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
            ["client_id"] = settings.ClientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
            values["client_secret"] = clientSecret;
        if (!string.IsNullOrWhiteSpace(codeVerifier))
            values["code_verifier"] = codeVerifier;

        using var response = await _httpClient.PostAsync(
            settings.TokenEndpoint,
            new FormUrlEncodedContent(values),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Token exchange failed ({(int)response.StatusCode}). {body}");

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
}
