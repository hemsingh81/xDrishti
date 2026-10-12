using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Bars.Dhan;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class RequestThrottleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_request_goes_at_once_and_the_next_ones_wait_for_their_slots()
    {
        var clock = new FakeTimeProvider();
        var throttle = new RequestThrottle(4, clock);

        await throttle.WaitAsync(Ct);
        var second = throttle.WaitAsync(Ct).AsTask();
        var third = throttle.WaitAsync(Ct).AsTask();

        second.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(249));
        second.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await second;
        third.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(250));
        await third;
    }

    [Fact]
    public async Task A_burst_never_sends_more_than_the_rate_in_a_second()
    {
        var clock = new FakeTimeProvider();
        var throttle = new RequestThrottle(5, clock);
        var sentAt = new List<TimeSpan>();
        var start = clock.GetUtcNow();

        var tasks = Enumerable.Range(0, 11).Select(async _ =>
        {
            await throttle.WaitAsync(Ct);
            lock (sentAt)
            {
                sentAt.Add(clock.GetUtcNow() - start);
            }
        }).ToList();
        for (var i = 0; i < 60; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Yield();
        }

        await Task.WhenAll(tasks);

        sentAt.Count.ShouldBe(11);
        sentAt.Order().Zip(sentAt.Order().Skip(5)).ShouldAllBe(pair => pair.Second - pair.First >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task After_a_quiet_time_the_next_request_goes_at_once()
    {
        var clock = new FakeTimeProvider();
        var throttle = new RequestThrottle(2, clock);
        await throttle.WaitAsync(Ct);
        clock.Advance(TimeSpan.FromMinutes(5));

        var again = throttle.WaitAsync(Ct);

        again.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task A_waiting_request_can_be_cancelled()
    {
        var clock = new FakeTimeProvider();
        var throttle = new RequestThrottle(1, clock);
        await throttle.WaitAsync(Ct);
        using var cancel = new CancellationTokenSource();

        var waiting = throttle.WaitAsync(cancel.Token).AsTask();
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(waiting);
    }

    [Fact]
    public void The_rate_must_be_at_least_one_per_second()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new RequestThrottle(0, TimeProvider.System));
    }
}
