using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>One leg a hand row expects: what it did, when (<c>HH:MM</c>, or <c>+1 HH:MM</c> of Tuesday), at what exact price and in what share of the full size; <see cref="Filled"/> only when the row has slippage.</summary>
public sealed record WantLeg(LegKind Kind, string At, double Price, decimal Share, double? Filled = null);

/// <summary>What a hand row says the trade is: how it ended, when, at what exact price, every figure of the trade to the printed precision, the legs and the counters of the adds.</summary>
public sealed record PyramidWant(
    ExitReason Reason,
    string At,
    double Price,
    double R,
    double Mae,
    double Mfe,
    WantLeg[] Legs,
    double? RFilled = null,
    int Taken = 0,
    int Missed = 0,
    int SkippedQuantity = 0,
    int Ignored = 0,
    bool StopGap = false,
    int Sessions = 1,
    int Day = 0);

/// <summary>
/// A hand row of P4-04 (criterion 2). The frame is that of <see cref="TrailRow"/> (a BUY at 100.00 entered at the open of the 09:45 minute with the stop 99.00, so that <c>R0</c> is 1.00, or the mirror SELL with the stop 101.00; the 0.1 % entry
/// limit and the tick 0.05) with the ladder of the spec unless a row says otherwise: <c>initial_pct</c> 50, <c>add_at_rr</c> 1 and 2, <c>add_pct</c> 30 and 20. The tape goes on with the bars of <see cref="Tape"/>; an ATR that is not a number gives
/// no trail level.
/// </summary>
public sealed record PyramidRow(
    string Name,
    Side Side,
    string[] Minutes,
    TrailBar[] Tape,
    PyramidWant Want,
    string[]? Tuesday = null,
    int Hold = 0,
    bool Modelled = false,
    string? EndsAfter = null,
    int MaxAdds = 2,
    long? FullSize = null,
    decimal Multiple = 1m,
    decimal? MinProgress = null,
    int TimeStopBars = 0,
    int TimeStopSessions = 0)
{
    public override string ToString() => Name;
}

/// <summary>Walks a <see cref="PyramidRow"/> through the real walker and checks every figure of the row.</summary>
internal static class PyramidHand
{
    private const double Tolerance = 1e-12;

    private static readonly DateOnly Monday = DayBuilder.Monday;

    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private static long Epoch(string at) => at.StartsWith("+1 ", StringComparison.Ordinal) ? DayBuilder.Epoch(Tuesday, at[3..]) : DayBuilder.Epoch(Monday, at);

    internal static PyramidPlan Plan(int maxAdds) => new(0.5m, [1m, 2m], [0.3m, 0.2m], maxAdds);

    internal static Result<WalkOutcome> Walk(PyramidRow row)
    {
        var (candidate, bars, rules, context) = Prepare(row);
        return TradeWalker.Walk(candidate, bars, rules, context);
    }

    /// <summary>The inputs of the walk of a row: the candidate, the minutes, the rules and the exit context.</summary>
    internal static (EntryCandidate Candidate, IReadOnlyList<Bar> Bars, WalkRules Rules, ExitContext Context) Prepare(PyramidRow row)
    {
        var bars = new List<Bar>();
        var monday = Day.Dense().Start(100.00).Set("09:45 100.00/100.00/100.00/100.00").Set(row.Minutes);
        if (row.EndsAfter is not null)
        {
            monday.EndsAfter(row.EndsAfter);
        }

        bars.AddRange(row.Tuesday is null ? monday.Bars() : [.. monday.Bars(), .. Day.After(monday, Tuesday).Set(row.Tuesday).Bars()]);
        var fills = row.Modelled ? FillRules.Default : Cand.NoSlippage;
        var rules = row.Hold > 0
            ? new WalkRules(null, WalkRules.NoSquareOff, fills, Cand.Tick05, SwingHold.Create(row.Hold, TestCalendars.DefaultWeek()).Value)
            : new WalkRules(null, 915, fills, Cand.Tick05);
        var policy = new LegPolicy(PartialKind.None, 0m, 0m, 0m, row.MinProgress, row.TimeStopBars, row.TimeStopSessions, Plan(row.MaxAdds));
        var full = row.FullSize;
        var context = new ExitContext(new AtrTrailRule(0, row.Multiple, 0m), TrailHand.TapeOf(row.Tape), policy, full is { } q ? (_, _) => q : null);
        return (TrailHand.Candidate(row.Side), bars, rules, context);
    }

    internal static void Check(PyramidRow row)
    {
        var walked = Walk(row);
        walked.IsSuccess.ShouldBeTrue(walked.IsFailure ? walked.Error.Message : string.Empty);
        var outcome = walked.Value;
        var want = row.Want;
        var trade = outcome.Trade.ShouldNotBeNull(row.Name);

        trade.ExitReason.ShouldBe(want.Reason, row.Name);
        trade.ExitEpochSeconds.ShouldBe(DayBuilder.Epoch(want.Day == 0 ? Monday : Tuesday, want.At), row.Name + " exit instant");
        trade.ExitPrice.ShouldBe(want.Price, Tolerance, row.Name + " exit price");
        trade.EntryPrice.ShouldBe(100.00, row.Name + " entry");
        trade.StopPrice.ShouldBe(row.Side == Side.Buy ? 99.00 : 101.00, row.Name + " the initial stop is what the trade shows");
        trade.TargetPrice.ShouldBeNull(row.Name);
        trade.RExact.ShouldBe(want.R, Tolerance, row.Name + " RExact");
        trade.RFilled.ShouldBe(want.RFilled ?? want.R, Tolerance, row.Name + " RFilled");
        trade.SlippageR.ShouldBe((want.RFilled ?? want.R) - want.R, Tolerance, row.Name + " SlippageR");
        trade.SlippageR.ShouldBeLessThanOrEqualTo(0.0);
        trade.MaeR.ShouldBe(want.Mae, Tolerance, row.Name + " MaeR");
        trade.MfeR.ShouldBe(want.Mfe, Tolerance, row.Name + " MfeR");
        trade.SessionsHeld.ShouldBe(want.Sessions, row.Name);

        var legs = trade.Legs.ShouldNotBeNull(row.Name + ": a pyramid is written with its legs");
        legs.Count.ShouldBe(want.Legs.Length, row.Name + " legs");
        for (var i = 0; i < legs.Count; i++)
        {
            var wantLeg = want.Legs[i];
            legs[i].Kind.ShouldBe(wantLeg.Kind, row.Name + " leg " + i);
            legs[i].EpochSeconds.ShouldBe(Epoch(wantLeg.At), row.Name + " leg " + i + " instant");
            legs[i].Price.ShouldBe(wantLeg.Price, Tolerance, row.Name + " leg " + i + " price");
            legs[i].Share.ShouldBe(wantLeg.Share, row.Name + " leg " + i + " share");
            if (wantLeg.Filled is { } filled)
            {
                legs[i].FilledPrice.ShouldBe(filled, Tolerance, row.Name + " leg " + i + " filled");
            }
        }

        legs[^1].Kind.ShouldBe(LegKind.Exit);
        legs[^1].Reason.ShouldBe(want.Reason.ToString());
        legs.Where(l => l.Kind != LegKind.Exit).Sum(l => l.Share).ShouldBe(legs[^1].Share, row.Name + ": the exit leg takes what the legs in hold");
        outcome.LegsTotal.ShouldBe(want.Legs.Length, row.Name);
        var counters = outcome.ToCounters();
        counters.AddsTaken.ShouldBe(want.Taken, row.Name + " adds taken");
        counters.AddsMissed.ShouldBe(want.Missed, row.Name + " adds missed");
        counters.AddsSkippedRisk.ShouldBe(0, row.Name + " adds skipped by risk");
        counters.AddsSkippedQuantity.ShouldBe(want.SkippedQuantity, row.Name + " adds skipped by quantity");
        outcome.StopLoosenIgnored.ShouldBe(want.Ignored, row.Name + " requests to loosen");
        outcome.StopGapExit.ShouldBe(want.StopGap, row.Name + " stop gap");
        legs.Count(l => l.Kind == LegKind.Add).ShouldBe(want.Taken, row.Name);
    }
}
