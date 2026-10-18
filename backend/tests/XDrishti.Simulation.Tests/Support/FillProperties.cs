using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The properties of P3-03 AC9 as ADR 0038 restates them (fill minute, entry price, exit price bounds, R and the excursions against a plain reference loop), written once and applied to the trades of
/// <c>Simulator.Run</c> (P3-03a AC16). They read the exact layer of a trade (<c>EntryPrice</c>, <c>ExitPrice</c>, <c>RExact</c>), which does not depend on the slippage.
/// </summary>
internal static class FillProperties
{
    /// <summary>The walker computes in decimal and the reference loop in double: the same figure to well within this.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>
    /// The index of the fill minute: the first minute after the candidate's that holds a trade (volume above 0). The trade fills in it, on its date, within the delay and before the square-off; at that minute's open
    /// when it is within the limit, otherwise at the limit, which the minute traded back to.
    /// </summary>
    public static int AssertFill(SimTrade trade, EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkOutcome outcome, int squareOffMinute)
    {
        var own = bars[candidate.DecisionBarIndex];
        var index = candidate.DecisionBarIndex + 1;
        while (bars[index].Volume == 0)
        {
            index++;
        }

        var fill = bars[index];

        trade.EntryEpochSeconds.ShouldBe(fill.EpochSeconds);
        SessionClock.DateOf(fill.EpochSeconds).ShouldBe(SessionClock.DateOf(own.EpochSeconds));
        (fill.EpochSeconds - candidate.DecisionEpochSeconds).ShouldBeInRange(0, SimulationEngine.MaxFillDelayMinutes * 60L);
        SessionClock.MinuteOfDay(fill.EpochSeconds).ShouldBeLessThan(squareOffMinute);
        trade.TradeDate.ShouldBe(SessionClock.DateOf(fill.EpochSeconds));
        outcome.EntryAfterMissingMinutes.ShouldBe(fill.EpochSeconds > candidate.DecisionEpochSeconds);
        if (trade.EntryFill == EntryFill.AtOpen)
        {
            trade.EntryPrice.ShouldBe(fill.Open);
        }
        else
        {
            trade.EntryPrice.ShouldBeInRange(fill.Low, fill.High);
        }

        return index;
    }

    /// <summary>The exit price lies in its minute or is the open or the close that the rule names; a square-off leaves at the open, at or after its minute; the end of the data at the close.</summary>
    public static void AssertExit(SimTrade trade, IReadOnlyList<Bar> bars, WalkOutcome outcome, int settled, int fillIndex, int squareOffMinute, string label)
    {
        var exit = bars[settled];

        settled.ShouldBeGreaterThanOrEqualTo(fillIndex, label);
        SessionClock.DateOf(exit.EpochSeconds).ShouldBe(trade.TradeDate, label);
        var inside = trade.ExitPrice >= exit.Low && trade.ExitPrice <= exit.High;
        var openOrClose = trade.ExitPrice == exit.Open || trade.ExitPrice == exit.Close;
        (inside || openOrClose || outcome.TargetGapExit).ShouldBeTrue($"{label}: {trade.ExitReason} at {trade.ExitPrice} in {exit}");
        if (trade.ExitReason == ExitReason.SquareOff)
        {
            trade.ExitPrice.ShouldBe(exit.Open, label);
            SessionClock.MinuteOfDay(exit.EpochSeconds).ShouldBeGreaterThanOrEqualTo(squareOffMinute, label);
        }

        if (trade.ExitReason == ExitReason.EndOfData)
        {
            trade.ExitPrice.ShouldBe(exit.Close, label);
        }
    }

    /// <summary>R, MAE and MFE equal the plain reference loop, the exit lies inside the excursions and the minutes examined are those from the fill minute to the exit minute.</summary>
    public static void AssertExcursions(SimTrade trade, IReadOnlyList<Bar> bars, WalkOutcome outcome, int settled, int fillIndex, string label)
    {
        var (r, mae, mfe) = Reference(trade, bars, fillIndex, settled);

        trade.RExact.ShouldBe(r, Tolerance, $"{label} R");
        trade.MaeR.ShouldBe(mae, Tolerance, $"{label} MAE");
        trade.MfeR.ShouldBe(mfe, Tolerance, $"{label} MFE");
        trade.MaeR.ShouldBeGreaterThanOrEqualTo(0);
        trade.MfeR.ShouldBeGreaterThanOrEqualTo(0);
        (-trade.MaeR).ShouldBeLessThanOrEqualTo(trade.RExact + Tolerance);
        trade.RExact.ShouldBeLessThanOrEqualTo(trade.MfeR + Tolerance);
        outcome.BarsExamined.ShouldBe(settled - fillIndex + 1, label);
    }

    /// <summary>
    /// The definition of D5 in the signed form: a move of a price is <c>s · (p − entry) / risk</c> with <c>s</c> = +1 for a buy and −1 for a sell; MFE is the best move seen and MAE the worst, never beyond 0; the exit
    /// minute contributes only what D5 allows (stop: the exit price; target: its adverse extreme and the target; square-off: its open; end of data: everything).
    /// </summary>
    private static (double R, double Mae, double Mfe) Reference(SimTrade trade, IReadOnlyList<Bar> bars, int fillIndex, int exitIndex)
    {
        var sign = trade.Side == Side.Buy ? 1.0 : -1.0;
        var risk = Math.Abs(trade.EntryPrice - trade.StopPrice);
        double Move(double price) => sign * (price - trade.EntryPrice) / risk;
        var best = 0.0;
        var worst = 0.0;
        void See(double price)
        {
            best = Math.Max(best, Move(price));
            worst = Math.Min(worst, Move(price));
        }

        for (var i = fillIndex; i <= exitIndex; i++)
        {
            var bar = bars[i];
            if (i < exitIndex || trade.ExitReason == ExitReason.EndOfData)
            {
                See(bar.Low);
                See(bar.High);
                continue;
            }

            switch (trade.ExitReason)
            {
                case ExitReason.Stop:
                    See(trade.ExitPrice);
                    break;
                case ExitReason.Target:
                    // only the adverse side of the minute counts, and only where it is below the entry (a minute that gapped beyond the target is wholly favourable)
                    worst = Math.Min(worst, Move(trade.Side == Side.Buy ? bar.Low : bar.High));
                    See(trade.ExitPrice);
                    break;
                case ExitReason.SquareOff:
                    See(bar.Open);
                    break;
            }
        }

        return (Move(trade.ExitPrice), -worst, best);
    }
}
