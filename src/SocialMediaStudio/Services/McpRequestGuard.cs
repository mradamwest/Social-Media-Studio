using System.Security.Cryptography;
using System.Text;

namespace SocialMediaStudio.Services;

/// <summary>
/// Authentication helper for the MCP transport. It deliberately operates only on the
/// dedicated MCP API key and never on social-network OAuth credentials.
/// </summary>
public sealed class McpRequestGuard
{
    private readonly McpServerOptions _options;

    public McpRequestGuard(McpServerOptions options)
    {
        _options = options;
        _options.Validate();
    }

    public bool IsAuthorized(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return IsLoopbackOnly();

        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var supplied = authorizationHeader["Bearer ".Length..].Trim();
        return FixedTimeEquals(supplied, _options.ApiKey);
    }

    private bool IsLoopbackOnly() =>
        _options.Host is "127.0.0.1" or "::1";

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
