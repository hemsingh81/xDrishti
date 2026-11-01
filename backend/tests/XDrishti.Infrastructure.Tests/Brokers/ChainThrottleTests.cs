using Microsoft.Extensions.Time.Testing;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>The one throttle of the option-chain endpoints (P14-02): never less than three seconds between any two requests of the process, whatever the callers ask for.</summary>
public sealed class ChainThrottleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_request_goes_at_once_and_each_later_one_waits_the_spacing_after_the_previous()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 5, 0, 0, TimeSpan.Zero));
        var waits = new AutoAdvancingClock(clock);
        var throttle = new ChainThrottle(waits);
        var times = new List<DateTimeOffset>();

        for (var i = 0; i < 4; i++)
        {
            await throttle.WaitAsync(TimeSpan.FromSeconds(3), Ct);
            times.Add(clock.GetUtcNow());
        }

        times[0].ShouldBe(new DateTimeOffset(2026, 10, 12, 5, 0, 0, TimeSpan.Zero));
        times.Zip(times.Skip(1), (a, b) => b - a).ShouldAllBe(gap => gap == TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2999)]
    public async Task A_spacing_under_three_seconds_is_three_seconds(int milliseconds)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 5, 0, 0, TimeSpan.Zero));
        var throttle = new ChainThrottle(new AutoAdvancingClock(clock));

        await throttle.WaitAsync(TimeSpan.FromMilliseconds(milliseconds), Ct);
        var first = clock.GetUtcNow();
        await throttle.WaitAsync(TimeSpan.FromMilliseconds(milliseconds), Ct);

        (clock.GetUtcNow() - first).ShouldBe(TimeSpan.FromSeconds(3));
        ChainThrottle.Floor.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task A_time_already_passed_costs_no_wait_and_a_doubled_spacing_applies_to_the_very_next_request()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 5, 0, 0, TimeSpan.Zero));
        var throttle = new ChainThrottle(new AutoAdvancingClock(clock));
        await throttle.WaitAsync(TimeSpan.FromSeconds(3), Ct);
        clock.Advance(TimeSpan.FromSeconds(10));
        var before = clock.GetUtcNow();

        await throttle.WaitAsync(TimeSpan.FromSeconds(3), Ct);
        (clock.GetUtcNow() - before).ShouldBe(TimeSpan.Zero);

        var last = clock.GetUtcNow();
        await throttle.WaitAsync(TimeSpan.FromSeconds(6), Ct);
        (clock.GetUtcNow() - last).ShouldBe(TimeSpan.FromSeconds(6));
    }
}
