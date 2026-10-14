using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Keeps the calls to Dhan's trade history and ledger apart (ADR 0025: at least <see cref="BrokerHistoryOptions.CallSpacingMs"/>, 250 ms by default): each caller waits for the next free slot. One instance per process (a singleton) so that two runs
/// that overlap still keep the spacing. Time comes from the <see cref="TimeProvider"/>.
/// </summary>
internal sealed class DhanHistoryThrottle(IOptions<BrokerHistoryOptions> options, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private long _next;

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        var spacing = TimeSpan.FromMilliseconds(options.Value.CallSpacingMs);
        if (spacing <= TimeSpan.Zero)
        {
            return;
        }

        long now, slot;
        lock (_gate)
        {
            now = clock.GetTimestamp();
            slot = Math.Max(now, _next);
            _next = slot + (long)(spacing.TotalSeconds * clock.TimestampFrequency);
        }

        if (slot > now)
        {
            await Task.Delay(clock.GetElapsedTime(now, slot), clock, cancellationToken);
        }
    }
}
