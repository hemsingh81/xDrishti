using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars;

namespace XDrishti.Api.Market;

/// <summary>
/// Runs the data jobs registered by the API, one after the other, in the background; first it ends the jobs an earlier process left "running". All logic is in
/// the command handlers.
/// </summary>
internal sealed partial class DataJobWorker(IDataJobChannel jobs, IServiceScopeFactory scopes, IApplicationInfo application, ILogger<DataJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
            await foreach (var ticket in jobs.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExecuteDataJobCommand>>();
                    await handler.HandleAsync(new ExecuteDataJobCommand(ticket.JobId), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // one failed job must not stop the worker; the handler has already recorded the failure
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogJobFailed(logger, ticket.JobId, ex);
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
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecoverInterruptedDataJobsCommand>>();
            await handler.HandleAsync(new RecoverInterruptedDataJobsCommand(application.StartedAt), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // recovery is housekeeping: the worker must still run (a stale job also expires after 12 hours)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRecoveryFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 3204, Level = LogLevel.Error, Message = "Interrupted data jobs could not be cleaned up")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Error, Message = "Data job {JobId} could not be executed")]
    private static partial void LogJobFailed(ILogger logger, Guid jobId, Exception exception);
}
