using Microsoft.Extensions.Time.Testing;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>The one throttle of the expired-options endpoint (P14-03): never less than half a second between any two requests of the process (2 a second at most), one a second by default.</summary>
public sealed class BackfillThrottleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_request_goes_at_once_and_each_later_one_waits_the_spacing_after_the_previous_which_is_one_a_second_on_the_default()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 11, 0, 0, TimeSpan.Zero));
        var throttle = new BackfillThrottle(new AutoAdvancingClock(clock));
        var times = new List<DateTimeOffset>();

        for (var i = 0; i < 10; i++)
        {
            await throttle.WaitAsync(TimeSpan.FromSeconds(1), Ct);
            times.Add(clock.GetUtcNow());
        }

        times[0].ShouldBe(new DateTimeOffset(2026, 10, 12, 11, 0, 0, TimeSpan.Zero));
        times.Zip(times.Skip(1), (a, b) => b - a).ShouldAllBe(gap => gap == TimeSpan.FromSeconds(1));
        (times[^1] - times[0]).ShouldBe(TimeSpan.FromSeconds(9), "ten requests in nine seconds: at most one a second");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(499)]
    public async Task A_spacing_under_half_a_second_is_half_a_second(int milliseconds)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 11, 0, 0, TimeSpan.Zero));
        var throttle = new BackfillThrottle(new AutoAdvancingClock(clock));

        await throttle.WaitAsync(TimeSpan.FromMilliseconds(milliseconds), Ct);
        var first = clock.GetUtcNow();
        await throttle.WaitAsync(TimeSpan.FromMilliseconds(milliseconds), Ct);

        (clock.GetUtcNow() - first).ShouldBe(TimeSpan.FromMilliseconds(500));
        BackfillThrottle.Floor.ShouldBe(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task A_time_already_passed_costs_no_wait_and_a_doubled_spacing_applies_to_the_very_next_request()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 11, 0, 0, TimeSpan.Zero));
        var throttle = new BackfillThrottle(new AutoAdvancingClock(clock));
        await throttle.WaitAsync(TimeSpan.FromSeconds(1), Ct);
        clock.Advance(TimeSpan.FromSeconds(10));
        var before = clock.GetUtcNow();

        await throttle.WaitAsync(TimeSpan.FromSeconds(1), Ct);
        (clock.GetUtcNow() - before).ShouldBe(TimeSpan.Zero);

        var last = clock.GetUtcNow();
        await throttle.WaitAsync(TimeSpan.FromSeconds(2), Ct);
        (clock.GetUtcNow() - last).ShouldBe(TimeSpan.FromSeconds(2));
    }
}
