using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace SocialMediaStudio.Services;

/// <summary>
/// Client for the app-owned OAuth broker. Provider app secrets stay on the server.
/// POST sessions with { provider, proofHash }; returns { sessionId, authorizationUrl }.
/// GET sessions/{sessionId} with X-Connection-Proof returns 202 while pending,
/// or { provider, accessToken, refreshToken, expiresAt } once completed.
/// The server must bind OAuth state to the session, verify the proof hash,
/// expire sessions, and deliver each completed session once only.
/// </summary>
public sealed class HostedAccountConnectionService
{
    public static string? ConfiguredEndpoint =>
        Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_CONNECTION_SERVICE_URL");

    public async Task<OAuthTokenResult> ConnectAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(ConfiguredEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new InvalidOperationException("The account connection service is not configured correctly.");

        var baseUri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
        var proof = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var proofHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(proof)));
        using var start = await client.PostAsJsonAsync("sessions", new { provider, proofHash }, timeout.Token);
        if (!start.IsSuccessStatusCode)
            throw new InvalidOperationException("Account sign-in could not be started. Please try again later.");
        var session = await start.Content.ReadFromJsonAsync<ConnectionSession>(cancellationToken: timeout.Token);
        if (session is null || string.IsNullOrWhiteSpace(session.SessionId) ||
            session.SessionId.Length > 128 || !session.SessionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
            !Uri.TryCreate(session.AuthorizationUrl, UriKind.Absolute, out var signIn) ||
            signIn.Scheme != Uri.UriSchemeHttps || signIn.Authority != baseUri.Authority ||
            !string.IsNullOrEmpty(signIn.UserInfo))
            throw new InvalidOperationException("The account connection service returned an invalid sign-in session.");

        Process.Start(new ProcessStartInfo(signIn.AbsoluteUri) { UseShellExecute = true });
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, "sessions/" + session.SessionId);
            request.Headers.Add("X-Connection-Proof", proof);
            using var response = await client.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Account sign-in expired or was declined. Please connect again.");
            var result = await response.Content.ReadFromJsonAsync<ConnectionResult>(cancellationToken: timeout.Token);
            if (result is null || !string.Equals(result.Provider, provider, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(result.AccessToken) ||
                (result.ExpiresAt is not null && result.ExpiresAt <= DateTimeOffset.UtcNow))
                throw new InvalidOperationException("Account sign-in returned an invalid connection.");
            return new OAuthTokenResult(result.AccessToken, result.RefreshToken, result.ExpiresAt);
        }
    }

    private sealed record ConnectionSession(string SessionId, string AuthorizationUrl);
    private sealed record ConnectionResult(string Provider, string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);
}
