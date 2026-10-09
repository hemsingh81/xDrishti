using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Heartbeats;

namespace XDrishti.Worker.Heartbeats;

/// <summary>Reports that the worker is alive, so the API and UI can show service health.</summary>
internal sealed partial class HeartbeatService(
    IServiceScopeFactory scopes,
    IApplicationInfo app,
    IOptions<HeartbeatOptions> options,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, options.Value.Interval);
        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            await BeatAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task BeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordHeartbeatCommand>>();
            var result = await handler.HandleAsync(new RecordHeartbeatCommand(app.Name, app.Instance, app.Version), cancellationToken);
            if (result.IsFailure)
            {
                LogRejected(logger, result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
#pragma warning disable CA1031 // a failed beat must never stop the service; it is retried on the next tick
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Heartbeat started, interval {Interval}")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat rejected: {Reason}")]
    private static partial void LogRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat failed; retrying on next tick")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
