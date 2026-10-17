using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Why a lagged call may not mix a feature of the date with a bar feature, not even on a 1D strategy (ADR 0034, Amendment 4). On a 1D strategy "the previous date" and "the previous bar" are the same moment while every
/// session has its closing minute, but only then: a session without its 15:29 minute closes its daily bar at the first minute of the next date, after the date features have moved on. So the pairing of the two clocks depends on
/// the data, it holds for one timeframe of nine, and the rule is kept uniform. The tests pin the facts (the frame is read as it is, nothing here asserts that the date value is "one day behind" the bar, which is a fixed
/// offset of the context and does not matter for the pairing).
/// </summary>
public sealed class DailyStrategyDateLagTests
{
    /// <summary>The VIX closed at 10 on Monday, 20 on Tuesday, 30 on Wednesday, 40 on Thursday and 50 on Friday; a date reads the day before it, so the VIX of a date is the previous close: 10 on Tuesday, 20 on Wednesday, and so on.</summary>
    private static MarketContext VixContext() =>
        MarketContext.Create(
            [
                new ContextDay(Clock.Mon, 100, 90, 95, 10),
                new ContextDay(Clock.Tue, 100, 90, 95, 20),
                new ContextDay(Clock.Wed, 100, 90, 95, 30),
                new ContextDay(Clock.Thu, 100, 90, 95, 40),
                new ContextDay(Clock.Fri, 100, 90, 95, 50),
            ],
            Contexts.Small).Value;

    private static readonly DateOnly[] Days = [Clock.Tue, Clock.Wed, Clock.Thu, Clock.Fri];

    /// <summary>The daily closes: the bars of a day have the same close, so the daily bar closes at that value whether it closes at 15:29 or late.</summary>
    private static readonly double[] Closes = [8, 25, 35, 50];

    /// <summary>Two minutes a day, the first (09:15) and the last (15:29) of the session, Tuesday to Friday; <paramref name="withoutClosingMinuteOn"/> is a date whose 15:29 minute is missing.</summary>
    private static Bar[] Bars(DateOnly? withoutClosingMinuteOn = null)
    {
        var bars = new List<Bar>();
        for (var i = 0; i < Days.Length; i++)
        {
            bars.Add(Clock.Close(Days[i], Clock.Open, Closes[i]));
            if (Days[i] != withoutClosingMinuteOn)
            {
                bars.Add(Clock.Close(Days[i], Clock.At(15, 29), Closes[i]));
            }
        }

        return [.. bars];
    }

    /// <summary>
    /// Runs the condition over the bars and keeps what the frame had at each step that closed a daily bar: the value of the VIX now and the value it had one date ago (what <c>vix[1]</c> reads), and the truth of the condition.
    /// </summary>
    private static List<(double Now, double Lag1, Truth Truth)> AtEveryDailyClose(string condition, DateOnly? withoutClosingMinuteOn)
    {
        var rig = Rig.Of(Timeframe.D1, condition, VixContext(), minimumDepth: 3);
        var vix = rig.Frame.HandleOf(Specs.Of("vix", Timeframe.M1));
        var closes = new List<(double, double, Truth)>();
        foreach (var bar in Bars(withoutClosingMinuteOn))
        {
            var truth = rig.Step(bar)[0];
            if (rig.Frame.BarClosed(Timeframe.D1))
            {
                closes.Add((rig.Frame.At(vix, 0), rig.Frame.At(vix, 1), truth));
            }
        }

        return closes;
    }

    [Fact]
    public void While_every_session_has_its_closing_minute_the_previous_date_is_the_value_the_previous_daily_close_saw()
    {
        // Four daily closes (Tue to Fri, at 15:29). The VIX read at each is 10, 20, 30, 40, and one date back it is NaN (no date before Tuesday), 10, 20, 30: the lag-1 value of a daily close is the value seen at the previous one.
        var closes = AtEveryDailyClose("close > 0 and vix > 0", withoutClosingMinuteOn: null);

        closes.Select(c => c.Now).ShouldBe([10, 20, 30, 40]);
        double.IsNaN(closes[0].Lag1).ShouldBeTrue("no date before the first");
        closes.Select(c => c.Lag1).Skip(1).ShouldBe(closes.Select(c => c.Now).Take(3));
    }

    [Fact]
    public void A_session_without_its_closing_minute_closes_its_daily_bar_after_the_date_moved_on_and_the_pairing_is_lost()
    {
        // Wednesday has no 15:29: its daily bar closes at 09:15 on Thursday, when the VIX is already Thursday's. The daily closes are then Tue 15:29, Thu 09:15, Thu 15:29 and Fri 15:29, the VIX read at them is 10, 30, 30, 40
        // and one date back it is NaN, 20, 20, 30. The lag-1 value is the previous date's, as always, but it is no longer the value the previous daily close saw: it fails at two of the three closes after the first.
        var closes = AtEveryDailyClose("close > 0 and vix > 0", withoutClosingMinuteOn: Clock.Wed);

        closes.Select(c => c.Now).ShouldBe([10, 30, 30, 40]);
        double.IsNaN(closes[0].Lag1).ShouldBeTrue("no date before the first");
        closes.Select(c => c.Lag1).Skip(1).ShouldBe([20, 20, 30]);
        closes.Select((c, i) => i > 0 && c.Lag1 == closes[i - 1].Now).ShouldBe([false, false, false, true]);
    }

    [Fact]
    public void The_hole_changes_the_reads_that_have_no_lag_as_well_so_it_is_not_a_property_of_lagged_calls()
    {
        // The explicit form of a crossing (the one the lint suggests): the close is above the VIX now and was not above it at the previous daily bar. With every closing minute it is false on Tuesday (8 against 10) and true on
        // the three days after. With the hole, Wednesday's bar closes on Thursday at 09:15 and is read against Thursday's VIX (25 against 30): false, so the answer is FFTT. Nothing here is lagged against the date, and the
        // answer still changes: a daily bar that closes late meets the date lane out of step, whatever is asked of it.
        const string Explicit = "close > vix and close[1] <= vix";

        string Truths(DateOnly? hole) => Rig.Word(AtEveryDailyClose(Explicit, hole).Select(c => c.Truth));

        Truths(null).ShouldBe("FTTT");
        Truths(Clock.Wed).ShouldBe("FFTT");
    }
}
