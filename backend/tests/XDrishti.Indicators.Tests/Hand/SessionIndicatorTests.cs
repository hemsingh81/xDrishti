using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for vwap, rel_volume, the opening range and the session context.</summary>
public sealed class SessionIndicatorTests
{
    private static readonly DateOnly Fri = new(2026, 3, 6);
    private static readonly DateOnly Mon = new(2026, 3, 9);
    private static readonly DateOnly Tue = new(2026, 3, 10);
    private static readonly DateOnly Thu = new(2026, 3, 12);

    private static List<double> Run(IIndicator indicator, IEnumerable<Bar> bars)
    {
        var values = new List<double>();
        foreach (var bar in bars)
        {
            indicator.Update(bar);
            values.Add(indicator.Value);
        }

        return values;
    }

    [Fact]
    public void Vwap_weights_the_typical_price_by_volume_and_ignores_zero_volume_bars()
    {
        var bars = new[]
        {
            Minutes.Bar(Fri, Minutes.Open, 10, 12, 10, 11, 100),       // typical 11
            Minutes.Bar(Fri, Minutes.Open + 1, 11, 14, 10, 12, 300),   // typical 12
            Minutes.Bar(Fri, Minutes.Open + 2, 12, 99, 1, 50, 0),      // no volume: no effect
        };

        var values = Run(new Vwap(), bars);

        values[0].ShouldBe(11.0);
        values[1].ShouldBe(11.75);   // (11 * 100 + 12 * 300) / 400
        values[2].ShouldBe(11.75);
    }

    [Fact]
    public void Vwap_starts_afresh_at_the_first_bar_of_the_next_session_and_waits_for_volume()
    {
        var bars = new[]
        {
            Minutes.Bar(Fri, Minutes.Open, 10, 12, 10, 11, 100),
            Minutes.Bar(Mon, Minutes.Open, 20, 20, 20, 20, 0),          // new session, no volume yet
            Minutes.Bar(Mon, Minutes.Open + 1, 20, 21, 19, 20, 50),     // typical 20
            Minutes.Bar(Mon, Minutes.Open + 2, 20, 24, 24, 24, 50),     // typical 24: (20 * 50 + 24 * 50) / 100
        };

        var indicator = new Vwap();
        var values = Run(indicator, bars);

        values[1].ShouldBeNaN();
        values[2].ShouldBe(20.0);
        values[3].ShouldBe(22.0);
        indicator.IsReady.ShouldBeTrue();
    }

    [Fact]
    public void Rel_volume_divides_by_the_mean_of_the_previous_n_bars_without_the_current_one()
    {
        var bars = new[] { 100L, 200, 300, 150 }.Select((v, i) => Minutes.Close(Fri, Minutes.Open + i, 10, v));

        var values = Run(new RelVolume(2), bars);

        values[1].ShouldBeNaN();
        values[2].ShouldBe(2.0);    // 300 / mean(100, 200) = 300 / 150
        values[3].ShouldBe(0.6);    // 150 / mean(200, 300) = 150 / 250
    }

    [Fact]
    public void Rel_volume_is_not_ready_while_the_previous_volumes_are_all_zero()
    {
        var bars = new[] { 0L, 0, 50, 10 }.Select((v, i) => Minutes.Close(Fri, Minutes.Open + i, 10, v));
        var indicator = new RelVolume(2);

        var values = Run(indicator, bars);

        values[2].ShouldBeNaN();    // mean(0, 0) = 0
        values[3].ShouldBe(10.0 / 25.0);
        indicator.IsReady.ShouldBeTrue();
    }

    [Fact]
    public void The_opening_range_is_not_ready_until_the_last_of_its_minutes_has_closed_then_constant_until_the_session_ends()
    {
        var bars = new[]
        {
            Minutes.Bar(Fri, Minutes.Open, 10, 10, 8, 9),
            Minutes.Bar(Fri, Minutes.Open + 1, 9, 12, 9, 11),
            Minutes.Bar(Fri, Minutes.Open + 2, 11, 11, 7, 8),
            Minutes.Bar(Fri, Minutes.Open + 3, 8, 20, 1, 15),     // after the range: no effect
            Minutes.Bar(Mon, Minutes.Open, 30, 33, 29, 31),       // next session: starts afresh
        };
        var high = new OrHigh(3);
        var low = new OrLow(3);

        var highs = Run(high, bars);
        var lows = Run(low, bars);

        highs.Take(2).ShouldAllBe(v => double.IsNaN(v));
        highs[2].ShouldBe(12.0);
        highs[3].ShouldBe(12.0);
        lows[2].ShouldBe(7.0);
        lows[3].ShouldBe(7.0);
        highs[4].ShouldBeNaN();
        high.IsReady.ShouldBeFalse();
        high.WarmUp.ShouldBe(3);
    }

    [Fact]
    public void The_opening_range_is_ready_after_a_missing_last_minute_once_a_later_bar_proves_the_window_is_over()
    {
        var bars = new[]
        {
            Minutes.Bar(Fri, Minutes.Open, 10, 10, 8, 9),
            Minutes.Bar(Fri, Minutes.Open + 1, 9, 12, 9, 11),
            // 09:17 is missing
            Minutes.Bar(Fri, Minutes.Open + 3, 8, 20, 1, 15),
        };

        var values = Run(new OrHigh(3), bars);

        values[1].ShouldBeNaN();
        values[2].ShouldBe(12.0);   // the bar at 09:18 starts after the range and is not part of it
    }

    [Fact]
    public void An_evening_session_has_no_opening_range_because_it_does_not_start_at_nine_fifteen()
    {
        var sunday = new DateOnly(2026, 3, 8);
        var bars = Enumerable.Range(0, 60).Select(i => Minutes.Close(sunday, Minutes.At(18, 15) + i, 100 + i));

        var values = Run(new OrHigh(15), bars);

        values.ShouldAllBe(v => double.IsNaN(v));
    }

    [Fact]
    public void The_opening_range_of_a_fifteen_minute_series_counts_whole_bars()
    {
        var bars = new[]
        {
            Minutes.Bar(Fri, Minutes.Open, 10, 15, 8, 9),
            Minutes.Bar(Fri, Minutes.Open + 15, 9, 18, 7, 11),
            Minutes.Bar(Fri, Minutes.Open + 30, 11, 30, 1, 11),
        };

        var values = Run(new OrHigh(30, 15), bars);

        values[0].ShouldBeNaN();
        values[1].ShouldBe(18.0);
        values[2].ShouldBe(18.0);
    }

    [Fact]
    public void Prev_day_is_the_previous_session_that_had_bars_across_a_weekend_and_a_holiday_gap()
    {
        var bars = new[]
        {
            Minutes.Bar(Fri, Minutes.Open, 100, 105, 99, 104, 10),
            Minutes.Bar(Fri, Minutes.Open + 1, 104, 106, 103, 105, 20),
            Minutes.Bar(Mon, Minutes.Open, 108, 109, 107, 108.5, 5),     // after the weekend
            Minutes.Bar(Mon, Minutes.Open + 1, 108.5, 112, 106, 110, 7),
            // Tuesday and Wednesday have no bars at all (holidays)
            Minutes.Bar(Thu, Minutes.Open, 111, 111, 111, 111, 1),
        };

        var fields = Enum.GetValues<SessionField>().ToDictionary(f => f, f => Run(new SessionContext(f), bars));

        fields[SessionField.PrevOpen][0].ShouldBeNaN();
        fields[SessionField.PrevClose][1].ShouldBeNaN();
        fields[SessionField.GapPct][1].ShouldBeNaN();
        fields[SessionField.PrevOpen][2].ShouldBe(100.0);
        fields[SessionField.PrevHigh][2].ShouldBe(106.0);
        fields[SessionField.PrevLow][2].ShouldBe(99.0);
        fields[SessionField.PrevClose][2].ShouldBe(105.0);
        fields[SessionField.PrevVolume][2].ShouldBe(30.0);
        // Thursday looks back to Monday, the last session with bars: open 108, high 112, low 106, close 110, volume 12
        fields[SessionField.PrevOpen][4].ShouldBe(108.0);
        fields[SessionField.PrevHigh][4].ShouldBe(112.0);
        fields[SessionField.PrevLow][4].ShouldBe(106.0);
        fields[SessionField.PrevClose][4].ShouldBe(110.0);
        fields[SessionField.PrevVolume][4].ShouldBe(12.0);
    }

    [Fact]
    public void Today_open_high_and_low_so_far_and_the_gap_in_percent()
    {
        var bars = new[]
        {
            Minutes.Bar(Fri, Minutes.Open, 100, 105, 99, 104, 10),
            Minutes.Bar(Fri, Minutes.Open + 1, 104, 106, 103, 105, 20),
            Minutes.Bar(Mon, Minutes.Open, 108, 109, 107, 108.5, 5),
            Minutes.Bar(Mon, Minutes.Open + 1, 108.5, 112, 106, 110, 7),
        };

        var open = Run(new SessionContext(SessionField.TodayOpen), bars);
        var high = Run(new SessionContext(SessionField.TodayHigh), bars);
        var low = Run(new SessionContext(SessionField.TodayLow), bars);
        var gap = Run(new SessionContext(SessionField.GapPct), bars);

        open.ShouldBe([100.0, 100, 108, 108]);
        high.ShouldBe([105.0, 106, 109, 112]);
        low.ShouldBe([99.0, 99, 107, 106]);
        gap[2].ShouldBe(300.0 / 105, 1e-12);    // (108 - 105) / 105 * 100
        gap[3].ShouldBe(300.0 / 105, 1e-12);    // fixed for the session
    }

    [Fact]
    public void A_gap_down_is_negative_and_a_zero_previous_close_gives_no_gap()
    {
        var down = new[] { Minutes.Close(Fri, Minutes.Open, 200), Minutes.Close(Mon, Minutes.Open, 190) };
        var zero = new[] { Minutes.Close(Fri, Minutes.Open, 0), Minutes.Close(Mon, Minutes.Open, 190) };

        Run(new SessionContext(SessionField.GapPct), down)[1].ShouldBe(-5.0, 1e-12);
        var indicator = new SessionContext(SessionField.GapPct);
        Run(indicator, zero)[1].ShouldBeNaN();
        indicator.IsReady.ShouldBeFalse();
    }
}
