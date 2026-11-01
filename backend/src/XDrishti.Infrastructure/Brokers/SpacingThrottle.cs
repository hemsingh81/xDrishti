namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// A throttle that keeps any two requests of the process at least <c>spacing</c> apart and never less than its <c>floor</c> (a singleton per endpoint family, so two callers still keep the gap). Time comes from the <see cref="TimeProvider"/>. A larger
/// spacing asked for after a rate limit applies to the very next request. The option-chain throttle (floor 3 s) and the backfill throttle (floor 0.5 s) are this class with their own floor.
/// </summary>
internal abstract class SpacingThrottle(TimeProvider clock, TimeSpan floor)
{
    private readonly Lock _gate = new();
    private long _last;
    private bool _started;

    /// <summary>Returns when the caller may send its request: <paramref name="spacing"/> (at least the floor) after the previous request of the process.</summary>
    public async ValueTask WaitAsync(TimeSpan spacing, CancellationToken cancellationToken)
    {
        var gap = spacing < floor ? floor : spacing;
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
