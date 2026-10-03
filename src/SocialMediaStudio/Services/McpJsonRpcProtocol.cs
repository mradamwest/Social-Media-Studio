using System.Text.Json;

namespace SocialMediaStudio.Services;

public static class McpJsonRpcProtocol
{
    public const string ProtocolVersion = "2025-06-18";

    public static object Initialize(JsonElement? id) => new
    {
        jsonrpc = "2.0",
        id = ToId(id),
        result = new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new { tools = new { listChanged = false } },
            serverInfo = new { name = "social-media-studio", version = "0.1.0" }
        }
    };

    public static object ToolList(JsonElement? id) => new
    {
        jsonrpc = "2.0",
        id = ToId(id),
        result = new
        {
            tools = new object[]
            {
                Tool("list_queued_posts", "List drafts and scheduled posts", new { type = "object", properties = new { } }),
                Tool("get_post_status", "Get a publishing-history entry by ID", new
                {
                    type = "object",
                    properties = new { id = new { type = "string" } },
                    required = new[] { "id" }
                }),
                Tool("publish_now", "Publish a post to selected networks", PostSchema()),
                Tool("create_post", "Create a draft post", PostSchema()),
                Tool("schedule_post", "Schedule a post", new
                {
                    type = "object",
                    properties = new
                    {
                        caption = new { type = "string" },
                        networks = new { type = "array", items = new { type = "string" } },
                        title = new { type = "string" },
                        mediaFiles = new { type = "array", items = new { type = "string" } },
                        scheduledFor = new { type = "string", format = "date-time" }
                    },
                    required = new[] { "networks", "scheduledFor" }
                }),
                Tool("delete_post", "Delete a queued draft or scheduled post", new
                {
                    type = "object",
                    properties = new { id = new { type = "string" } },
                    required = new[] { "id" }
                })
            }
        }
    };

    public static object Error(JsonElement? id, int code, string message) => new
    {
        jsonrpc = "2.0",
        id = ToId(id),
        error = new { code, message = Sanitize(message) }
    };

    private static object Tool(string name, string description, object inputSchema) =>
        new { name, description, inputSchema };

    private static object PostSchema() => new
    {
        type = "object",
        properties = new
        {
            caption = new { type = "string" },
            networks = new { type = "array", items = new { type = "string" } },
            title = new { type = "string" },
            mediaFiles = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "networks" }
    };

    private static object? ToId(JsonElement? id) => id is null ? null : id.Value.ValueKind switch
    {
        JsonValueKind.String => id.Value.GetString(),
        JsonValueKind.Number when id.Value.TryGetInt64(out var n) => n,
        _ => null
    };

    private static string Sanitize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "MCP request failed." :
        value.Length <= 300 ? value : value[..300] + "…";
}
