using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// Criterion 19 of P3-03b, literally, for one walk of a <see cref="SeededDay"/>: it returns the violations (an empty list is a pass) instead of throwing, so that the same check is the property over the 500 days and the
/// signal-minute control of criterion 20(c)(ii), which must fail on at least ten seeds. Every scan is the test's own, written from the rules and not from the walker: the fill minute is the first minute after the
/// candidate's own that holds a trade; the square-off is the first minute that holds a trade at or after <c>Q</c>.
/// <list type="bullet">
/// <item>The fill minute holds a trade (Volume above 0), starts at or after the <b>true</b> decision instant and at most 5 minutes after it, is on the candidate's date, before <c>Q</c>, and is the first such minute.</item>
/// <item>The exit minute is on the entry date, holds a trade and is not before the fill minute; a <c>SquareOff</c> is at the first minute that holds a trade at or after <c>Q</c>; an <c>EndOfData</c> exit is at the last
/// minute of the date that holds a trade and only where there is no minute at or after <c>Q</c>; a position never lives past that minute.</item>
/// <item>Every exact fill price lies in its minute's [low, high] or is that order's own level (a target gapped through lies outside the minute by design).</item>
/// <item>In <c>Modelled</c> mode every filled price is a multiple of the tick and <c>abs(filled − exact) ≤ a + tick</c> with <c>a</c> = 0 for a resting limit (the entry at the limit and the target); in <c>Exact</c> mode the
/// filled price is the exact one.</item>
/// <item><c>risk &gt; 0</c> (AC5) and <c>−MAE ≤ RExact ≤ MFE</c>.</item>
/// </list>
/// </summary>
internal static class FillTimingCheck
{
    private const double Epsilon = 1e-9;

    /// <param name="day">The seeded day: its minutes, its tick and its square-off minute.</param>
    /// <param name="truth">The candidate as the series really decided it: its own minute and its true decision instant. A control passes the real one while the walk used a doctored one.</param>
    /// <param name="outcome">What the walker gave.</param>
    /// <param name="fills">The fill rules of the walk.</param>
    public static List<string> Violations(SeededDay day, EntryCandidate truth, WalkOutcome outcome, FillRules fills)
    {
        var found = new List<string>();
        if (outcome.Trade is not { } trade)
        {
            return found;
        }

        void Expect(bool ok, string what)
        {
            if (!ok)
            {
                found.Add($"seed {day.Seed} {trade.Side} {trade.ExitReason}: {what}");
            }
        }

        var bars = day.Bars;
        var tick = (double)day.Tick;
        var own = bars[truth.DecisionBarIndex];
        var date = SessionClock.DateOf(own.EpochSeconds);

        // the fill minute
        var fillIndex = IndexOf(bars, trade.EntryEpochSeconds);
        var firstTrade = FirstIndex(bars, truth.DecisionBarIndex + 1, b => b.Volume > 0);
        Expect(fillIndex >= 0, "the fill minute is one of the minutes");
        if (fillIndex < 0)
        {
            return found;
        }

        var fill = bars[fillIndex];
        Expect(fill.Volume > 0, "the fill minute holds a trade");
        Expect(fill.EpochSeconds >= truth.DecisionEpochSeconds, "the fill minute starts at or after the decision instant");
        Expect(fill.EpochSeconds - truth.DecisionEpochSeconds <= SimulationEngine.MaxFillDelayMinutes * 60L, "the fill minute is at most 5 minutes after the decision instant");
        Expect(SessionClock.DateOf(fill.EpochSeconds) == date, "the fill minute is on the candidate's date");
        Expect(SessionClock.MinuteOfDay(fill.EpochSeconds) < day.SquareOffMinute, "the fill minute is before the square-off");
        Expect(fillIndex == firstTrade, "the fill minute is the first minute after the candidate's that holds a trade");

        // the exit minute and the square-off scan
        var exitIndex = IndexOf(bars, trade.ExitEpochSeconds);
        Expect(exitIndex >= 0, "the exit minute is one of the minutes");
        if (exitIndex < 0)
        {
            return found;
        }

        var exit = bars[exitIndex];
        var squareOff = FirstIndex(bars, fillIndex + 1, b => b.Volume > 0 && SessionClock.DateOf(b.EpochSeconds) == date && SessionClock.MinuteOfDay(b.EpochSeconds) >= day.SquareOffMinute);
        var lastOfDate = LastIndex(bars, fillIndex, b => b.Volume > 0 && SessionClock.DateOf(b.EpochSeconds) == date);
        Expect(SessionClock.DateOf(exit.EpochSeconds) == date, "the exit minute is on the entry date");
        Expect(exit.Volume > 0, "the exit minute holds a trade");
        Expect(exitIndex >= fillIndex, "the exit minute is not before the fill minute");
        Expect(squareOff < 0 || exitIndex <= squareOff, "a position does not live past the first minute that holds a trade at or after the square-off");
        if (trade.ExitReason == ExitReason.SquareOff)
        {
            Expect(exitIndex == squareOff, "a square-off is at the first minute that holds a trade at or after Q");
        }

        if (trade.ExitReason == ExitReason.EndOfData)
        {
            Expect(squareOff < 0 && exitIndex == lastOfDate, "an end-of-data exit is at the last minute of the date that holds a trade, and only where none is at or after Q");
        }

        // the exact prices
        Expect(trade.EntryFill == EntryFill.AtOpen ? trade.EntryPrice == fill.Open : trade.EntryPrice >= fill.Low && trade.EntryPrice <= fill.High, "the exact entry is the open, or lies in the fill minute when it is at the limit");
        var exitInRange = trade.ExitPrice >= exit.Low && trade.ExitPrice <= exit.High;
        var ownLevel = trade.ExitReason == ExitReason.Target && trade.ExitPrice == trade.TargetPrice;
        Expect(exitInRange || ownLevel, $"the exact exit {trade.ExitPrice} lies in its minute [{exit.Low}, {exit.High}] or is the target");

        // the filled prices
        var slippage = fills.Slippage;
        if (fills.Basis == FillBasis.Exact)
        {
            Expect(trade.Fills == FillBasis.Exact && trade.EntryFilledPrice == trade.EntryPrice && trade.ExitFilledPrice == trade.ExitPrice, "with no slippage the filled prices are the exact ones");
        }
        else
        {
            var entryAmount = trade.EntryFill == EntryFill.AtLimit ? 0 : (slippage.Ticks * tick) + (slippage.BarRangeFrac * (fill.High - fill.Low));
            var exitAmount = trade.ExitReason switch
            {
                ExitReason.Target => 0,
                ExitReason.Stop => ((slippage.Ticks + slippage.StopExitExtraTicks) * tick) + (slippage.BarRangeFrac * (exit.High - exit.Low)),
                _ => (slippage.Ticks * tick) + (slippage.BarRangeFrac * (exit.High - exit.Low)),
            };
            Expect(trade.Fills == FillBasis.Modelled, "a run with slippage says its fills are modelled");
            Expect(OnGrid(trade.EntryFilledPrice, day.Tick), $"the filled entry {trade.EntryFilledPrice} is a multiple of the tick {day.Tick}");
            Expect(OnGrid(trade.ExitFilledPrice, day.Tick), $"the filled exit {trade.ExitFilledPrice} is a multiple of the tick {day.Tick}");
            Expect(Math.Abs(trade.EntryFilledPrice - trade.EntryPrice) <= entryAmount + tick + Epsilon, "the filled entry is within a + tick of the exact one");
            Expect(Math.Abs(trade.ExitFilledPrice - trade.ExitPrice) <= exitAmount + tick + Epsilon, "the filled exit is within a + tick of the exact one");
        }

        // the unit and the excursions
        Expect(Math.Abs(trade.EntryPrice - trade.StopPrice) > 0, "the risk is above zero");
        Expect(-trade.MaeR <= trade.RExact + Epsilon && trade.RExact <= trade.MfeR + Epsilon, $"-MAE {-trade.MaeR} <= RExact {trade.RExact} <= MFE {trade.MfeR}");

        return found;
    }

    /// <summary>A price is on the grid of the tick when it is a whole number of ticks (decimal, rounded to 8 places like the walker).</summary>
    public static bool OnGrid(double price, decimal tick) => decimal.Round((decimal)price, 8) % tick == 0m;

    private static int IndexOf(IReadOnlyList<Bar> bars, long epoch)
    {
        for (var i = 0; i < bars.Count; i++)
        {
            if (bars[i].EpochSeconds == epoch)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FirstIndex(IReadOnlyList<Bar> bars, int from, Func<Bar, bool> predicate)
    {
        for (var i = Math.Max(0, from); i < bars.Count; i++)
        {
            if (predicate(bars[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int LastIndex(IReadOnlyList<Bar> bars, int from, Func<Bar, bool> predicate)
    {
        var last = -1;
        for (var i = Math.Max(0, from); i < bars.Count; i++)
        {
            if (predicate(bars[i]))
            {
                last = i;
            }
        }

        return last;
    }
}
