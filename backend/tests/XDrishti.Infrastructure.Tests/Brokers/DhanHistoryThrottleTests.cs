using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>Calls to the trade history and the ledger are spaced by at least 250 ms (ADR 0025, P2-13 non-functional); a spacing of 0 switches it off.</summary>
public sealed class DhanHistoryThrottleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Calls_are_at_least_250_ms_apart_by_default()
    {
        var clock = new FakeTimeProvider();
        var throttle = new DhanHistoryThrottle(Options.Create(new BrokerHistoryOptions()), new AutoAdvancingClock(clock));
        var start = clock.GetUtcNow();
        var times = new List<TimeSpan>();

        for (var i = 0; i < 4; i++)
        {
            await throttle.WaitAsync(Ct);
            times.Add(clock.GetUtcNow() - start);
        }

        times.ShouldBe([TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750)]);
    }

    [Fact]
    public async Task A_spacing_of_zero_never_waits()
    {
        var clock = new FakeTimeProvider();
        var throttle = new DhanHistoryThrottle(Options.Create(new BrokerHistoryOptions { CallSpacingMs = 0 }), clock);

        for (var i = 0; i < 10; i++)
        {
            await throttle.WaitAsync(Ct);
        }

        clock.GetUtcNow().ShouldBe(new FakeTimeProvider().GetUtcNow());
    }

    [Fact]
    public async Task A_cancelled_wait_is_not_swallowed()
    {
        var clock = new FakeTimeProvider();
        var throttle = new DhanHistoryThrottle(Options.Create(new BrokerHistoryOptions()), clock);
        await throttle.WaitAsync(Ct);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await throttle.WaitAsync(cancelled.Token));
    }
}
