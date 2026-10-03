using System.Text.Json;

namespace SocialMediaStudio.Services;

public sealed class McpToolDispatcher
{
    private readonly McpToolService _tools;

    public McpToolDispatcher(McpToolService tools) => _tools = tools;

    public async Task<object> DispatchAsync(
        JsonElement? id,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            object result = toolName switch
            {
                "list_queued_posts" => _tools.ListQueuedPosts(),
                "list_publishing_history" => _tools.ListPublishingHistory(),
                "get_post_status" => _tools.GetPublishingStatus(RequiredString(arguments, "id"))
                    ?? throw new KeyNotFoundException("Publishing status was not found."),
                "create_post" => _tools.CreateDraft(
                    OptionalString(arguments, "caption"),
                    RequiredStrings(arguments, "networks"),
                    OptionalString(arguments, "title"),
                    OptionalStrings(arguments, "mediaFiles")),
                "schedule_post" => _tools.SchedulePost(
                    OptionalString(arguments, "caption"),
                    RequiredStrings(arguments, "networks"),
                    RequiredDateTimeOffset(arguments, "scheduledFor"),
                    OptionalString(arguments, "title"),
                    OptionalStrings(arguments, "mediaFiles")),
                "edit_post" => _tools.EditQueuedPost(
                    RequiredString(arguments, "id"),
                    OptionalString(arguments, "caption"),
                    RequiredStrings(arguments, "networks"),
                    OptionalDateTimeOffset(arguments, "scheduledFor"),
                    OptionalString(arguments, "title"),
                    OptionalStrings(arguments, "mediaFiles")),
                "delete_post" => new { deleted = _tools.DeleteQueuedPost(RequiredString(arguments, "id")) },
                "publish_now" => await _tools.PublishNowAsync(
                    OptionalString(arguments, "caption"),
                    RequiredStrings(arguments, "networks"),
                    OptionalString(arguments, "title"),
                    OptionalStrings(arguments, "mediaFiles"),
                    cancellationToken),
                _ => throw new InvalidOperationException("Unknown MCP tool.")
            };

            return Success(id, result);
        }
        catch (Exception ex)
        {
            return McpJsonRpcProtocol.Error(id, -32602, ex.Message);
        }
    }

    private static object Success(JsonElement? id, object value) => new
    {
        jsonrpc = "2.0",
        id = IdValue(id),
        result = new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(value) }
            }
        }
    };

    private static string RequiredString(JsonElement args, string name) =>
        OptionalString(args, name) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"{name} is required.");

    private static string? OptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static IReadOnlyList<string> RequiredStrings(JsonElement args, string name)
    {
        var values = OptionalStrings(args, name);
        return values is { Count: > 0 }
            ? values
            : throw new ArgumentException($"{name} requires at least one value.");
    }

    private static IReadOnlyList<string>? OptionalStrings(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            return null;

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();
    }

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement args, string name)
    {
        var raw = OptionalString(args, name);
        if (raw is null) return null;
        return DateTimeOffset.TryParse(raw, out var value)
            ? value
            : throw new ArgumentException($"{name} must be an ISO 8601 date-time.");
    }

    private static DateTimeOffset RequiredDateTimeOffset(JsonElement args, string name)
    {
        var raw = RequiredString(args, name);
        return DateTimeOffset.TryParse(raw, out var value)
            ? value
            : throw new ArgumentException($"{name} must be an ISO 8601 date-time.");
    }

    private static object? IdValue(JsonElement? id) => id is null ? null : id.Value.ValueKind switch
    {
        JsonValueKind.String => id.Value.GetString(),
        JsonValueKind.Number when id.Value.TryGetInt64(out var n) => n,
        _ => null
    };
}
