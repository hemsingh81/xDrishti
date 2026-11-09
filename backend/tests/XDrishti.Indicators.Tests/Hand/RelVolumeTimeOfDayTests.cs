using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for <c>rel_volume_tod(n)</c> (ADR 0233).</summary>
public sealed class RelVolumeTimeOfDayTests
{
    private static readonly DateOnly[] Days = [new(2026, 3, 9), new(2026, 3, 10), new(2026, 3, 11), new(2026, 3, 12)];

    private static Bar At(DateOnly day, int minuteOffset, long volume) => Minutes.Bar(day, Minutes.Open + minuteOffset, 10, 11, 9, 10, volume);

    [Fact]
    public void The_bar_is_divided_by_the_mean_of_the_same_minute_of_the_previous_n_sessions()
    {
        var indicator = new RelVolumeTimeOfDay(2);
        long[] open = [100, 300, 600];
        long[] later = [10, 10, 10];

        for (var d = 0; d < 3; d++)
        {
            indicator.Update(At(Days[d], 0, open[d]));
            var atOpen = indicator.Value;
            indicator.Update(At(Days[d], 1, later[d]));

            if (d < 2)
            {
                double.IsNaN(atOpen).ShouldBeTrue("fewer than n sessions seen at this time of day");
            }
            else
            {
                atOpen.ShouldBe(3.0);   // 600 / mean(100, 300) = 600 / 200
            }
        }

        indicator.WarmUp.ShouldBe((2 * 375) + 1);
    }

    [Fact]
    public void The_afternoon_bars_of_yesterday_are_not_the_baseline_of_the_opening_bar()
    {
        var own = new RelVolume(1);
        var timeOfDay = new RelVolumeTimeOfDay(1);

        // Day 1: opening bar 1000, last bar 10. Day 2 opening bar 500.
        own.Update(At(Days[0], 0, 1000));
        timeOfDay.Update(At(Days[0], 0, 1000));
        own.Update(At(Days[0], 374, 10));
        timeOfDay.Update(At(Days[0], 374, 10));
        own.Update(At(Days[1], 0, 500));
        timeOfDay.Update(At(Days[1], 0, 500));

        own.Value.ShouldBe(50.0);        // against yesterday's last bar: the old, unchanged meaning
        timeOfDay.Value.ShouldBe(0.5);   // against yesterday's opening bar
    }

    [Fact]
    public void A_zero_mean_and_a_bar_outside_the_session_are_not_ready_and_the_value_never_reads_ahead()
    {
        var indicator = new RelVolumeTimeOfDay(1);
        indicator.Update(At(Days[0], 0, 0));
        indicator.Update(At(Days[1], 0, 5));
        indicator.IsReady.ShouldBeFalse("mean 0");

        indicator.Update(Minutes.Bar(Days[2], Minutes.Open - 5, 10, 11, 9, 10, 5));
        indicator.IsReady.ShouldBeFalse("before the open");

        // The same prefix gives the same value whatever follows it.
        var a = new RelVolumeTimeOfDay(1);
        var b = new RelVolumeTimeOfDay(1);
        a.Update(At(Days[0], 3, 40));
        b.Update(At(Days[0], 3, 40));
        a.Update(At(Days[1], 3, 20));
        b.Update(At(Days[1], 3, 20));
        b.Update(At(Days[2], 3, 99_999));
        a.Value.ShouldBe(0.5);
    }

    [Fact]
    public void Five_minute_bars_use_their_own_slots_and_reset_forgets_the_history()
    {
        var indicator = new RelVolumeTimeOfDay(1, 5);
        indicator.Update(At(Days[0], 5, 200));
        indicator.Update(At(Days[1], 5, 100));
        indicator.Value.ShouldBe(0.5);

        indicator.Reset();
        indicator.Update(At(Days[2], 5, 100));
        indicator.IsReady.ShouldBeFalse();
    }

    [Fact]
    public void The_oldest_session_leaves_the_mean_when_a_new_one_arrives()
    {
        var indicator = new RelVolumeTimeOfDay(2);
        var seen = new List<double>();
        foreach (var (day, volume) in new[] { (0, 100L), (1, 300L), (2, 600L), (3, 1000L) })
        {
            indicator.Update(At(Days[day], 0, volume));
            seen.Add(indicator.Value);
        }

        seen[2].ShouldBe(3.0);                          // 600 / mean(100, 300)
        seen[3].ShouldBe(1000.0 / 450.0, 1e-12);        // 1000 / mean(300, 600): the 100 has left
    }

    [Fact]
    public void A_session_that_lacks_the_bar_is_skipped_and_the_ring_reaches_one_session_further_back()
    {
        var indicator = new RelVolumeTimeOfDay(1);
        indicator.Update(At(Days[0], 7, 100));
        indicator.Update(At(Days[1], 8, 999));   // day 2 has no bar at offset 7
        indicator.Update(At(Days[3], 7, 50));

        indicator.Value.ShouldBe(0.5);           // against day 1, the last session that had the minute
    }

    [Fact]
    public void The_clipped_last_bar_of_an_hourly_and_a_quarter_hourly_session_has_its_own_slot()
    {
        var hourly = new RelVolumeTimeOfDay(1, 60);
        hourly.WarmUp.ShouldBe(7 + 1);           // ceil(375 / 60) = 7 bars a session
        foreach (var offset in new[] { 0, 60, 120, 180, 240, 300, 360 })
        {
            hourly.Update(At(Days[0], offset, 100));
            double.IsNaN(hourly.Value).ShouldBeTrue();
        }

        hourly.Update(At(Days[1], 360, 50));     // 15:15, the bar clipped at the close
        hourly.Value.ShouldBe(0.5);

        var quarter = new RelVolumeTimeOfDay(1, 15);
        quarter.WarmUp.ShouldBe(25 + 1);
        quarter.Update(At(Days[0], 360, 100));
        quarter.Update(At(Days[1], 360, 300));
        quarter.Value.ShouldBe(3.0);
    }

    [Fact]
    public void The_largest_period_is_ready_only_after_250_sessions()
    {
        var indicator = new RelVolumeTimeOfDay(250);
        var first = new DateOnly(2025, 1, 1);
        for (var d = 0; d < 250; d++)
        {
            indicator.Update(At(first.AddDays(d), 0, 100));
            indicator.IsReady.ShouldBeFalse();
        }

        indicator.Update(At(first.AddDays(250), 0, 200));

        indicator.Value.ShouldBe(2.0);
        indicator.WarmUp.ShouldBe((250 * 375) + 1);
    }
}
