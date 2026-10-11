using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Refresh;

namespace XDrishti.Api.Market;

/// <summary>
/// Runs the refreshes registered by the API, one after the other, in the background; first it ends the refreshes an earlier process left
/// "running". All logic is in the command handlers.
/// </summary>
internal sealed partial class RefreshJobWorker(IRefreshJobChannel jobs, IServiceScopeFactory scopes, IApplicationInfo application, ILogger<RefreshJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
            await foreach (var job in jobs.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExecuteRefreshCommand>>();
                    await handler.HandleAsync(new ExecuteRefreshCommand(job.RunId), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // one failed job must not stop the worker; the handler has already recorded the failure
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogJobFailed(logger, job.RunId, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecoverInterruptedRefreshesCommand>>();
            await handler.HandleAsync(new RecoverInterruptedRefreshesCommand(application.StartedAt), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // recovery is housekeeping: the worker must still run (stale runs also expire after 30 minutes)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRecoveryFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 3202, Level = LogLevel.Error, Message = "Interrupted refreshes could not be cleaned up")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Error, Message = "Refresh job {RunId} could not be executed")]
    private static partial void LogJobFailed(ILogger logger, Guid runId, Exception exception);
}
