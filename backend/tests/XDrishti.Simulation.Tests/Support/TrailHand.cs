using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>One closed bar of the tape of a hand row: the instant it closed (<c>HH:MM</c> of Monday, or <c>+1 HH:MM</c> of Tuesday), its close and the value the rule reads (the ATR, the N-bar extreme or the EMA).</summary>
public sealed record TrailBar(string At, double Close, double Value);

/// <summary>What a hand row says the trade is: how it ended, when (on the exit day, 0 for Monday), at what exact price, and every figure of the trade to the printed precision.</summary>
public sealed record TrailWant(ExitReason Reason, string At, double Price, double R, double Mae, double Mfe, double? RFilled = null, int Ignored = 0, bool StopGap = false, int Sessions = 1, int Day = 0, double Entry = 100.00);

/// <summary>
/// A hand row of P4-03 (criteria 2 to 4). The frame is the same in every row: a BUY at 100.00 entered at the open of the 09:45 minute (stop 99.00, so the initial risk is 1.00) or its mirror SELL at 100.00 (stop 101.00), a trigger bar that closed
/// at 100.00, the 0.1 % entry limit, the tick 0.05. <see cref="Minutes"/> are the minutes that are not flat: a minute that is not listed repeats the previous close; the tape starts, as the scanner's does, with the decision bar (closed at 09:45, never
/// read) and goes on with the bars of <see cref="Tape"/>.
/// </summary>
public sealed record TrailRow(
    string Name,
    Side Side,
    string[] Minutes,
    TrailBar[] Tape,
    TrailWant Want,
    string[]? Tuesday = null,
    int Hold = 0,
    bool Modelled = false,
    string? EndsAfter = null,
    double Multiple = 0,
    double Activate = 0)
{
    public override string ToString() => Name;
}

/// <summary>Walks a <see cref="TrailRow"/> through the real walker with the rule it is given and checks every figure of the row.</summary>
internal static class TrailHand
{
    private const double Tolerance = 1e-12;

    private static readonly DateOnly Monday = DayBuilder.Monday;

    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private static long Epoch(string at) => at.StartsWith("+1 ", StringComparison.Ordinal) ? DayBuilder.Epoch(Tuesday, at[3..]) : DayBuilder.Epoch(Monday, at);

    internal static EntryCandidate Candidate(Side side) => side == Side.Buy
        ? Cand.Make(Side.Buy, 29, "09:45", 99.00, "09:30", (99.50, 100.00, 99.50, 100.00))
        : Cand.Make(Side.Sell, 29, "09:45", 101.00, "09:30", (100.50, 100.50, 100.00, 100.00));

    internal static ExitTape TapeOf(TrailBar[] bars)
    {
        var tape = new ExitTape(["v"]);
        tape.Append(DayBuilder.Epoch(Monday, "09:45"), 100.00, [double.NaN]);
        foreach (var bar in bars)
        {
            tape.Append(Epoch(bar.At), bar.Close, [bar.Value]);
        }

        return tape;
    }

    internal static IReadOnlyList<Bar> Bars(TrailRow row)
    {
        var monday = Day.Dense().Start(100.00).Set("09:45 100.00/100.00/100.00/100.00").Set(row.Minutes);
        if (row.EndsAfter is not null)
        {
            monday.EndsAfter(row.EndsAfter);
        }

        return row.Tuesday is null ? monday.Bars() : [.. monday.Bars(), .. Day.After(monday, Tuesday).Set(row.Tuesday).Bars()];
    }

    internal static Result<WalkOutcome> Walk(TrailRow row, IExitRule rule, LegPolicy? policy = null)
    {
        var fills = row.Modelled ? FillRules.Default : Cand.NoSlippage;
        var rules = row.Hold > 0
            ? new WalkRules(null, WalkRules.NoSquareOff, fills, Cand.Tick05, SwingHold.Create(row.Hold, TestCalendars.DefaultWeek()).Value)
            : new WalkRules(null, 915, fills, Cand.Tick05);
        return TradeWalker.Walk(Candidate(row.Side), Bars(row), rules, new ExitContext(rule, TapeOf(row.Tape), policy));
    }

    internal static void Check(TrailRow row, IExitRule rule, LegPolicy? policy = null)
    {
        var walked = Walk(row, rule, policy);
        walked.IsSuccess.ShouldBeTrue(walked.IsFailure ? walked.Error.Message : string.Empty);
        var outcome = walked.Value;
        var want = row.Want;
        var trade = outcome.Trade.ShouldNotBeNull(row.Name);

        trade.ExitReason.ShouldBe(want.Reason, row.Name);
        trade.ExitEpochSeconds.ShouldBe(DayBuilder.Epoch(want.Day == 0 ? Monday : Tuesday, want.At), row.Name + " exit instant");
        trade.ExitPrice.ShouldBe(want.Price, Tolerance, row.Name + " exit price");
        trade.EntryPrice.ShouldBe(want.Entry, row.Name + " entry");
        trade.StopPrice.ShouldBe(row.Side == Side.Buy ? 99.00 : 101.00, row.Name + " the initial stop is what the trade shows");
        trade.TargetPrice.ShouldBeNull(row.Name);
        trade.RExact.ShouldBe(want.R, Tolerance, row.Name + " RExact");
        trade.RFilled.ShouldBe(want.RFilled ?? want.R, Tolerance, row.Name + " RFilled");
        trade.SlippageR.ShouldBe((want.RFilled ?? want.R) - want.R, Tolerance, row.Name + " SlippageR");
        trade.SlippageR.ShouldBeLessThanOrEqualTo(0.0);
        trade.MaeR.ShouldBe(want.Mae, Tolerance, row.Name + " MaeR");
        trade.MfeR.ShouldBe(want.Mfe, Tolerance, row.Name + " MfeR");
        trade.SessionsHeld.ShouldBe(want.Sessions, row.Name);
        trade.Legs.ShouldBeNull(row.Name + ": a trail leaves in one fill");
        outcome.LegsTotal.ShouldBe(0);
        outcome.StopLoosenIgnored.ShouldBe(want.Ignored, row.Name + " requests to loosen");
        outcome.StopGapExit.ShouldBe(want.StopGap, row.Name + " stop gap");
        outcome.ToCounters().ExitsEmaExit.ShouldBe(want.Reason == ExitReason.EmaExit ? 1 : 0, row.Name);
    }
}
