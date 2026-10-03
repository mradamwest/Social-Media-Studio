using System.Windows;
using SocialMediaStudio.Services;

namespace SocialMediaStudio;

public partial class App : Application
{
    private CancellationTokenSource? _mcpCancellation;
    private McpHttpServer? _mcpServer;
    private CancellationTokenSource? _schedulerCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartMcpServerIfEnabled();
        StartScheduledPublishing();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _schedulerCancellation?.Cancel();
        _schedulerCancellation?.Dispose();
        _mcpCancellation?.Cancel();
        _mcpServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _mcpCancellation?.Dispose();
        base.OnExit(e);
    }

    private void StartScheduledPublishing()
    {
        var tokens = new SecureTokenStore();
        var coordinator = new PublishingCoordinator(new ISocialPublisher[]
        {
            new BlueskyPublisher(tokens), new XPublisher(tokens), new ThreadsPublisher(tokens),
            new FacebookPublisher(tokens), new InstagramPublisher(tokens), new YouTubePublisher(tokens)
        });
        var executor = new ScheduledPostExecutor(new PostQueueService(), new AutomationPublishingService(coordinator));
        executor.ResetInterruptedItems();
        _schedulerCancellation = new CancellationTokenSource();
        var cancellation = _schedulerCancellation;
        _ = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                try { await executor.ExecuteDueAsync(cancellationToken: cancellation.Token); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                catch { /* keep scheduler alive; per-post failures are recorded by the executor */ }
                try { await Task.Delay(TimeSpan.FromSeconds(30), cancellation.Token); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            }
        }, cancellation.Token);
    }

    private void StartMcpServerIfEnabled()
    {
        var options = McpServerOptions.FromEnvironment();
        if (!options.Enabled) return;
        options.Validate();

        var tokens = new SecureTokenStore();
        var coordinator = new PublishingCoordinator(new ISocialPublisher[]
        {
            new BlueskyPublisher(tokens),
            new XPublisher(tokens),
            new ThreadsPublisher(tokens),
            new FacebookPublisher(tokens),
            new InstagramPublisher(tokens),
            new YouTubePublisher(tokens)
        });

        var tools = new McpToolService(
            new AutomationPublishingService(coordinator),
            new PostQueueService());

        _mcpServer = new McpHttpServer(options, tools);
        _mcpCancellation = new CancellationTokenSource();
        var server = _mcpServer;
        var cancellation = _mcpCancellation;

        _ = Task.Run(async () =>
        {
            try { await server.RunAsync(cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show(
                    $"MCP server could not start: {ex.Message}",
                    "Social Media Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            }
        });
    }
}
