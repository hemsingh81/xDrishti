using XDrishti.Application.Live;

namespace XDrishti.Application.Tests.Live;

/// <summary>The back-off (1, 2, 4 ... 30 s, jittered) and the flapping limit (P2-10 criterion 5).</summary>
public sealed class ReconnectPolicyTests
{
    private static readonly TimeSpan One = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan Max = TimeSpan.FromSeconds(30);

    [Fact]
    public void The_wait_doubles_from_one_second_up_to_the_maximum()
    {
        var backoff = new ReconnectBackoff(() => 0d);

        var waits = Enumerable.Range(0, 8).Select(_ => backoff.Next(One, Max).TotalSeconds).ToList();

        waits.ShouldBe([1, 2, 4, 8, 16, 30, 30, 30]);
    }

    [Fact]
    public void The_jitter_adds_up_to_a_fifth_and_never_passes_the_maximum()
    {
        var backoff = new ReconnectBackoff(() => 0.5d);

        var first = backoff.Next(One, Max);
        for (var i = 0; i < 10; i++)
        {
            backoff.Next(One, Max);
        }

        first.TotalSeconds.ShouldBe(1.1, 0.0001);
        backoff.Next(One, Max).ShouldBe(Max);
    }

    [Fact]
    public void Reset_starts_again_from_one_second()
    {
        var backoff = new ReconnectBackoff(() => 0d);
        backoff.Next(One, Max);
        backoff.Next(One, Max);

        backoff.Reset();

        backoff.Next(One, Max).ShouldBe(One);
    }

    [Fact]
    public void Ten_reconnects_in_five_minutes_are_allowed_and_the_eleventh_is_flapping()
    {
        var limiter = new ReconnectLimiter();
        var start = new DateTimeOffset(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

        var results = Enumerable.Range(0, 11).Select(i => limiter.Record(start.AddSeconds(i * 20))).ToList();

        results.Take(10).ShouldAllBe(flapping => !flapping);
        results[10].ShouldBeTrue();
    }

    [Fact]
    public void Reconnects_spread_over_more_than_five_minutes_do_not_add_up()
    {
        var limiter = new ReconnectLimiter();
        var start = new DateTimeOffset(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

        var results = Enumerable.Range(0, 30).Select(i => limiter.Record(start.AddSeconds(i * 40))).ToList();

        results.ShouldAllBe(flapping => !flapping);
    }
}
