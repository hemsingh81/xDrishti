using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>P5-02 D5: at most four progress events a second for a run.</summary>
public sealed class ProgressThrottleTests
{
    [Fact]
    public void The_first_event_passes_and_the_next_waits_a_quarter_of_a_second()
    {
        var time = new FakeTimeProvider();
        var throttle = new ProgressThrottle(time, ProgressThrottle.QuarterSecond);

        throttle.TryPass().ShouldBeTrue();
        time.Advance(TimeSpan.FromMilliseconds(249));
        throttle.TryPass().ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));
        throttle.TryPass().ShouldBeTrue();
    }

    [Fact]
    public void Events_every_ten_milliseconds_for_ten_seconds_pass_at_most_four_in_any_second()
    {
        var time = new FakeTimeProvider();
        var throttle = new ProgressThrottle(time, ProgressThrottle.QuarterSecond);
        var passed = new List<TimeSpan>();
        var start = time.GetTimestamp();

        for (var i = 0; i < 1000; i++)
        {
            if (throttle.TryPass())
            {
                passed.Add(time.GetElapsedTime(start));
            }

            time.Advance(TimeSpan.FromMilliseconds(10));
        }

        passed.Count.ShouldBe(40);
        for (var i = 0; i + 4 < passed.Count; i++)
        {
            (passed[i + 4] - passed[i]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        }
    }
}
