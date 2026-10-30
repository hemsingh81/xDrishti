using XDrishti.Execution.Application.Confirm;

namespace XDrishti.Execution.Host;

/// <summary>
/// The expiry loop of the confirm tickets (spec P12-08 criterion 11): every 2 seconds it closes the unanswered tickets past their expiry <c>expired</c>. Closing a ticket is the safe direction; with no ticket (no account can be
/// <c>semi_auto</c> yet) it reads one empty set and writes nothing. It sends nothing and holds no broker port.
/// </summary>
internal sealed partial class TicketExpiryService(TicketCloser closer, ILogger<TicketExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TicketCloser.Interval);
        do
        {
            try
            {
                await closer.ExpireDueAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 6760, Level = LogLevel.Warning, Message = "The ticket expiry pass failed ({Kind}); trying again")]
    private partial void LogFailed(string kind);
}
