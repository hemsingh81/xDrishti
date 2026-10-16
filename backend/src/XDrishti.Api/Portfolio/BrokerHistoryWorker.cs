using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Api.Portfolio;

/// <summary>
/// Carries out the trades and ledger runs that "Sync now" registered (ADR 0025), one after the other, in the background. All logic is in <see cref="ExecuteBrokerHistoryCommand"/>, which ends its run as failed whatever happens (also when the application is stopping: "interrupted"); a run that was still waiting in the queue, or lost with the process, is ended as interrupted at the next start by
/// the cleanup of interrupted syncs.
/// </summary>
internal sealed partial class BrokerHistoryWorker(IBrokerHistoryChannel runs, IServiceScopeFactory scopes, ILogger<BrokerHistoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var ticket in runs.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExecuteBrokerHistoryCommand>>();
                    await handler.HandleAsync(new ExecuteBrokerHistoryCommand(ticket), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // one failed run must not stop the worker; the use case has already ended the run as failed
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogRunFailed(logger, ticket.RunId, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    [LoggerMessage(EventId = 4211, Level = LogLevel.Error, Message = "Broker history run {RunId} could not be carried out")]
    private static partial void LogRunFailed(ILogger logger, Guid runId, Exception exception);
}
