using Microsoft.Extensions.Time.Testing;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// A <see cref="FakeTimeProvider"/> for code that waits on its clock (a request throttle, a retry backoff) without a test having to poll for it: when the code
/// starts a timer, the clock moves on by exactly the time the timer waits for, so the wait ends at once and the clock reads what it would have after a real
/// wait. Time is read from, and advanced on, the wrapped clock, which a test can read to see when things happened.
/// </summary>
internal sealed class AutoAdvancingClock(FakeTimeProvider clock) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();

    public override long GetTimestamp() => clock.GetTimestamp();

    public override long TimestampFrequency => clock.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = clock.CreateTimer(callback, state, dueTime, period);
        if (dueTime > TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
        {
            // On the pool, as a real timer would fire: the caller has the timer in hand before it can elapse.
            ThreadPool.QueueUserWorkItem(static wait => wait.Clock.Advance(wait.Due), (Clock: clock, Due: dueTime), preferLocal: false);
        }

        return timer;
    }
}
