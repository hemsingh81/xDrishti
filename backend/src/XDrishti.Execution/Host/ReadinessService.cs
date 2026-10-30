using XDrishti.Domain.Market;
using XDrishti.Execution.Application.Readiness;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Host;

/// <summary>
/// Drives the <see cref="ReadinessLoop"/>: wakes every 5 seconds (and at once on the owner's check now), but waits for the 09:10:30 mark so that it is written within 5 seconds of its time. The loop itself decides whether anything is read; with no account at assisted it reads the
/// calendar and the account list only.
/// </summary>
internal sealed partial class ReadinessService(ReadinessLoop loop, ReadinessSignal signal, TimeProvider clock, ILogger<ReadinessService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await loop.RunOnceAsync(signal.Consume(), stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e.GetType().Name);
            }

            try
            {
                var now = clock.GetUtcNow();
                var untilMark = ReadinessDayMark.MarkTime(IndiaTime.Today(now)) - now;
                await signal.WaitAsync(untilMark > TimeSpan.Zero && untilMark < Tick ? untilMark : Tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(EventId = 6744, Level = LogLevel.Warning, Message = "The readiness pass failed ({Kind}); trying again")]
    private partial void LogFailed(string kind);
}
