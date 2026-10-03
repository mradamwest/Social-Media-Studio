using System.Diagnostics;
using System.Net;
using System.Text;

namespace SocialMediaStudio.Services;

public sealed record OAuthProviderSettings(
    string Provider,
    string ClientId,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string Scope,
    string RedirectPath = "/callback/");

public sealed record OAuthAuthorizationResult(
    string Code,
    string? State,
    string RedirectUri);

public sealed class OAuthConnectionService
{
    public async Task<OAuthAuthorizationResult> AuthorizeAsync(
        OAuthProviderSettings settings,
        CancellationToken cancellationToken = default)
    {
        var state = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var port = GetFreePort();
        var redirectUri = $"http://127.0.0.1:{port}{settings.RedirectPath}";
        var authorizationUrl =
            $"{settings.AuthorizationEndpoint}?response_type=code" +
            $"&client_id={Uri.EscapeDataString(settings.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(settings.Scope)}" +
            $"&state={Uri.EscapeDataString(state)}";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });

        using var registration = cancellationToken.Register(listener.Stop);
        var context = await listener.GetContextAsync();
        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];

        const string response = "<html><body><h2>Social Media Studio</h2><p>Connection received. You can return to the app.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(response);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();

        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("The provider did not return an authorization code.");
        if (!string.Equals(state, returnedState, StringComparison.Ordinal))
            throw new InvalidOperationException("OAuth state validation failed.");

        return new OAuthAuthorizationResult(code, returnedState, redirectUri);
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
