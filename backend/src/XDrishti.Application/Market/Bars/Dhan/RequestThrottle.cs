namespace XDrishti.Application.Market.Bars.Dhan;

/// <summary>
/// Keeps requests to a broker at most <c>requestsPerSecond</c> per second: each caller waits for the next free slot, one slot every 1/rate seconds.
/// Time comes from the <see cref="TimeProvider"/>, so tests do not wait. Safe for concurrent callers.
/// </summary>
public sealed class RequestThrottle
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly long _interval;
    private long _next;

    public RequestThrottle(int requestsPerSecond, TimeProvider clock)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requestsPerSecond, 1);
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _interval = clock.TimestampFrequency / requestsPerSecond;
        _next = clock.GetTimestamp();
    }

    /// <summary>Returns when the caller may send its request.</summary>
    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        long now, slot;
        lock (_gate)
        {
            now = _clock.GetTimestamp();
            slot = Math.Max(now, _next);
            _next = slot + _interval;
        }

        if (slot > now)
        {
            await Task.Delay(_clock.GetElapsedTime(now, slot), _clock, cancellationToken);
        }
    }
}
