using System.Net;

namespace SocialMediaStudio.Services;

public sealed record McpServerOptions(
    bool Enabled,
    string Host,
    int Port,
    string Path,
    string? ApiKey)
{
    public static McpServerOptions FromEnvironment()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_MCP_ENABLED"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var host = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_MCP_HOST");
        if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";

        var path = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_MCP_PATH");
        if (string.IsNullOrWhiteSpace(path)) path = "/mcp";
        if (!path.StartsWith('/')) path = "/" + path;

        var port = 8765;
        var rawPort = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_MCP_PORT");
        if (!string.IsNullOrWhiteSpace(rawPort) &&
            int.TryParse(rawPort, out var parsed) &&
            parsed is > 0 and <= 65535)
            port = parsed;

        var apiKey = Environment.GetEnvironmentVariable("SOCIAL_MEDIA_STUDIO_MCP_API_KEY");
        return new McpServerOptions(enabled, host, port, path, apiKey);
    }

    public void Validate()
    {
        if (!Enabled) return;
        if (Port is <= 0 or > 65535)
            throw new InvalidOperationException("MCP port is invalid.");

        if (!IPAddress.TryParse(Host, out var address))
            throw new InvalidOperationException("MCP host must be an IP address.");

        if (!IPAddress.IsLoopback(address) && string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                "A non-loopback MCP listener requires SOCIAL_MEDIA_STUDIO_MCP_API_KEY.");
    }

    public string Prefix
    {
        get
        {
            Validate();
            var host = Host is "0.0.0.0" ? "+" : Host;
            return $"http://{host}:{Port}/";
        }
    }
}
