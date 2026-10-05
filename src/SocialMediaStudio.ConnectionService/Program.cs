using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SocialMediaStudio.Services;

var builder = WebApplication.CreateBuilder(args);
// OAuth callbacks contain authorization codes; suppress request URL logging.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("connections", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseRateLimiter();
var configuredOrigin = builder.Configuration["CONNECTION_SERVICE_PUBLIC_URL"];
if (!Uri.TryCreate(configuredOrigin, UriKind.Absolute, out var origin) ||
    origin.Scheme != "https" || !string.IsNullOrEmpty(origin.UserInfo) ||
    origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
    throw new InvalidOperationException("CONNECTION_SERVICE_PUBLIC_URL must be the service's HTTPS origin.");
var sessions = new ConcurrentDictionary<string, Session>();
var states = new ConcurrentDictionary<string, string>();
var exchange = new OAuthTokenExchangeService();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await next();
});

app.MapPost("/sessions", (StartRequest request) =>
{
    RemoveExpired();
    if (sessions.Count >= 1000) return Results.StatusCode(503);
    if (string.IsNullOrWhiteSpace(request.Provider) || !ProviderConnectionCatalog.Definitions.TryGetValue(request.Provider, out var definition) ||
        request.ProofHash is null || request.ProofHash.Length != 64 || !request.ProofHash.All(Uri.IsHexDigit))
        return Results.BadRequest();
    if (!TryBrokerSettings(definition.Provider, out _))
        return Results.Problem("This provider has not been configured by the app publisher.", statusCode: 503);
    var secretSetting = SecretSetting(definition);
    if (secretSetting is not null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(secretSetting)))
        return Results.Problem("This provider has not been configured by the app publisher.", statusCode: 503);
    var id = RandomValue();
    var state = RandomValue();
    var verifier = RandomValue();
    var session = new Session(definition.Provider, request.ProofHash, state, verifier, DateTimeOffset.UtcNow.AddMinutes(5));
    sessions[id] = session;
    states[state] = id;
    return Results.Json(new { sessionId = id, authorizationUrl = new Uri(origin, "authorize/" + id).AbsoluteUri });
}).RequireRateLimiting("connections");

app.MapGet("/authorize/{id}", (string id) =>
{
    if (!sessions.TryGetValue(id, out var session) || session.ExpiresAt <= DateTimeOffset.UtcNow || Volatile.Read(ref session.CallbackStarted) != 0)
        return Results.BadRequest("This connection has expired.");
    if (!TryBrokerSettings(session.Provider, out var settings))
        return Results.StatusCode(503);
    var values = new Dictionary<string, string>
    {
        ["response_type"] = "code", [settings!.ClientIdParameter] = settings.ClientId,
        ["redirect_uri"] = RedirectUri(session.Provider),
        ["scope"] = string.Join(settings.ScopeSeparator, settings.Scope.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)),
        ["state"] = session.State
    };
    if (settings.RequestOfflineAccess) { values["access_type"] = "offline"; values["prompt"] = "consent"; }
    if (settings.UsePkce)
    {
        values["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(session.Verifier)));
        values["code_challenge_method"] = "S256";
    }
    var query = string.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    return Results.Redirect(settings.AuthorizationEndpoint + "?" + query);
}).RequireRateLimiting("connections");

app.MapGet("/oauth/callback/{provider}", async (string provider, HttpContext context) =>
{
    var state = context.Request.Query["state"].ToString();
    if (string.IsNullOrWhiteSpace(state) || !states.TryGetValue(state, out var id) ||
        !sessions.TryGetValue(id, out var session) || session.ExpiresAt <= DateTimeOffset.UtcNow ||
        !string.Equals(session.Provider, provider, StringComparison.OrdinalIgnoreCase) ||
        Interlocked.CompareExchange(ref session.CallbackStarted, 1, 0) != 0)
        return Results.BadRequest("This connection is invalid or expired.");
    var code = context.Request.Query["code"].ToString();
    if (string.IsNullOrWhiteSpace(code) || context.Request.Query.ContainsKey("error"))
    {
        session.Failed = true;
        return Results.Text("Sign-in was declined. Return to Social Media Studio.");
    }
    try
    {
        if (!TryBrokerSettings(session.Provider, out var settings)) throw new InvalidOperationException();
        var definition = ProviderConnectionCatalog.Definitions[session.Provider];
        var secretSetting = SecretSetting(definition);
        var secret = secretSetting is null ? null : Environment.GetEnvironmentVariable(secretSetting);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = await exchange.ExchangeAsync(settings!, code, RedirectUri(session.Provider), secret,
            settings!.UsePkce ? session.Verifier : null, timeout.Token);
        session.Token = token;
        return Results.Text("Sign-in completed. Return to Social Media Studio.");
    }
    catch
    {
        session.Failed = true;
        return Results.Text("Sign-in could not be completed. Return to Social Media Studio and try again.", statusCode: 400);
    }
}).RequireRateLimiting("connections");

app.MapGet("/sessions/{id}", (string id, HttpContext context) =>
{
    if (!sessions.TryGetValue(id, out var session) || session.ExpiresAt <= DateTimeOffset.UtcNow) return Results.NotFound();
    var proof = context.Request.Headers["X-Connection-Proof"].ToString();
    if (proof.Length != 64 || !proof.All(Uri.IsHexDigit) || !CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(proof)), Convert.FromHexString(session.ProofHash)))
        return Results.Unauthorized();
    if (session.Failed) { Remove(id, session); return Results.StatusCode(403); }
    var token = session.Token;
    if (token is null) return Results.StatusCode(202);
    if (!sessions.TryRemove(id, out _)) return Results.NotFound();
    states.TryRemove(session.State, out _);
    return Results.Json(new { provider = session.Provider, token.AccessToken, token.RefreshToken, token.ExpiresAt });
}).RequireRateLimiting("connections");

// Refresh is initially enabled for YouTube only, matching the desktop publishing path.
app.MapPost("/refresh", async (RefreshRequest request) =>
{
    if (!string.Equals(request.Provider, "YouTube", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 2048)
        return Results.BadRequest();
    if (!TryBrokerSettings("YouTube", out var settings)) return Results.StatusCode(503);
    var secret = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_GOOGLE_CLIENT_SECRET");
    if (string.IsNullOrWhiteSpace(secret)) return Results.StatusCode(503);
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = await exchange.RefreshAsync(settings!, request.RefreshToken, secret, timeout.Token);
        return Results.Json(new { provider = "YouTube", token.AccessToken, token.RefreshToken, token.ExpiresAt });
    }
    catch { return Results.StatusCode(401); }
}).RequireRateLimiting("connections");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
string RedirectUri(string provider) => new Uri(origin, "oauth/callback/" + Uri.EscapeDataString(provider)).AbsoluteUri;
string? SecretSetting(ProviderConnectionDefinition definition) => definition.Provider switch
{
    "TikTok" => "SOCIAL_MEDIA_STUDIO_TIKTOK_CLIENT_SECRET",
    "LinkedIn" => "SOCIAL_MEDIA_STUDIO_LINKEDIN_CLIENT_SECRET",
    "YouTube" or "Google Business Profile" => "SOCIAL_MEDIA_STUDIO_GOOGLE_CLIENT_SECRET",
    _ => definition.ClientSecretSetting
};
bool TryBrokerSettings(string provider, out OAuthProviderSettings? settings)
{
    if (!ProviderConnectionCatalog.TryCreateSettings(provider, out settings)) return false;
    if (provider.Equals("LinkedIn", StringComparison.OrdinalIgnoreCase))
        settings = settings! with { AuthorizationEndpoint = "https://www.linkedin.com/oauth/v2/authorization", UsePkce = false };
    return true;
}
void Remove(string id, Session session) { sessions.TryRemove(id, out _); states.TryRemove(session.State, out _); }
void RemoveExpired() { foreach (var pair in sessions) if (pair.Value.ExpiresAt <= DateTimeOffset.UtcNow) Remove(pair.Key, pair.Value); }
static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
record StartRequest(string Provider, string ProofHash);
record RefreshRequest(string Provider, string RefreshToken);
sealed class Session(string provider, string proofHash, string state, string verifier, DateTimeOffset expiresAt)
{
    public string Provider { get; } = provider;
    public string ProofHash { get; } = proofHash;
    public string State { get; } = state;
    public string Verifier { get; } = verifier;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public int CallbackStarted;
    public volatile bool Failed;
    public volatile OAuthTokenResult? Token;
}
