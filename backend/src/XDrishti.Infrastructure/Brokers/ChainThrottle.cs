namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The one throttle of the option-chain endpoints (P14-02): any two requests of the process - an expiry list or a chain, of any underlying - are at least <c>spacing</c> apart, and never less than <see cref="Floor"/> (Dhan: one unique request every 3
/// seconds; whether that is per user is UNVERIFIED, so the strictest reading is kept). A singleton, so two callers still keep the gap. Time comes from the <see cref="TimeProvider"/>.
/// </summary>
internal sealed class ChainThrottle(TimeProvider clock)
{
    /// <summary>The lowest gap there is, whatever a caller asks for.</summary>
    public static readonly TimeSpan Floor = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();
    private long _last;
    private bool _started;

    /// <summary>Returns when the caller may send its request: <paramref name="spacing"/> (at least <see cref="Floor"/>) after the previous request of the process, whatever its kind. A larger spacing (after a rate limit) therefore takes effect on the very next request.</summary>
    public async ValueTask WaitAsync(TimeSpan spacing, CancellationToken cancellationToken)
    {
        var gap = spacing < Floor ? Floor : spacing;
        long now, slot;
        lock (_gate)
        {
            now = clock.GetTimestamp();
            slot = _started ? Math.Max(now, _last + (long)(gap.TotalSeconds * clock.TimestampFrequency)) : now;
            _started = true;
            _last = slot;
        }

        if (slot > now)
        {
            await Task.Delay(clock.GetElapsedTime(now, slot), clock, cancellationToken);
        }
    }
}
