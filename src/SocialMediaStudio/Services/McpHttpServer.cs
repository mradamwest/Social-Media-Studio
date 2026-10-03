using System.Net;
using System.Text;
using System.Text.Json;

namespace SocialMediaStudio.Services;

public sealed class McpHttpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly McpServerOptions _options;
    private readonly McpRequestGuard _guard;
    private readonly McpToolDispatcher _dispatcher;

    public McpHttpServer(McpServerOptions options, McpToolService tools)
    {
        _options = options;
        _options.Validate();
        _guard = new McpRequestGuard(options);
        _dispatcher = new McpToolDispatcher(tools);
        _listener.Prefixes.Add(options.Prefix);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;
        _listener.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = HandleAsync(context, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_listener.IsListening) _listener.Stop();
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.Equals(context.Request.Url?.AbsolutePath, _options.Path, StringComparison.Ordinal))
            {
                context.Response.StatusCode = 404;
                return;
            }

            if (!_guard.IsAuthorized(context.Request.Headers["Authorization"]))
            {
                context.Response.StatusCode = 401;
                context.Response.Headers["WWW-Authenticate"] = "Bearer";
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                return;
            }

            using var document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
            var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;

            object response = method switch
            {
                "initialize" => McpJsonRpcProtocol.Initialize(id),
                "tools/list" => McpJsonRpcProtocol.ToolList(id),
                "tools/call" => await DispatchToolAsync(id, root, cancellationToken),
                _ => McpJsonRpcProtocol.Error(id, -32601, "Method not found.")
            };

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = 400;
        }
        catch
        {
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private Task<object> DispatchToolAsync(
        JsonElement? id,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("params", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
            return Task.FromResult(McpJsonRpcProtocol.Error(id, -32602, "Tool name is required."));

        var name = nameElement.GetString()!;
        var arguments = parameters.TryGetProperty("arguments", out var args)
            ? args.Clone()
            : JsonDocument.Parse("{}").RootElement.Clone();

        return _dispatcher.DispatchAsync(id, name, arguments, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        return ValueTask.CompletedTask;
    }
}
