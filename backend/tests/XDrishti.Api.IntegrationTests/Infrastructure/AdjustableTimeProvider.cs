namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The real clock plus an offset that a test can move forward (session expiry) and must put back (<see cref="Reset"/>).
/// Tests of one collection run one after another, so a test that restores the offset in <c>finally</c> affects no one else.
/// </summary>
public sealed class AdjustableTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    /// <summary>Makes the clock read <paramref name="instant"/> now (and run on from there). Sign in after pinning: sessions are judged by this clock.</summary>
    public void Pin(DateTimeOffset instant) => Interlocked.Exchange(ref _offsetTicks, (instant - base.GetUtcNow()).Ticks);

    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
}
