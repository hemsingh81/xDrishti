using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>What the two loops (the intraday walk and the swing walk) build their results with: the trade, the no-trade and the failures that are not an outcome.</summary>
internal static class WalkResults
{
    /// <summary>
    /// Builds the trade. <paramref name="low"/> and <paramref name="high"/> are the extreme prices that count for the excursions, already restricted to what the exit minute may contribute. <paramref name="sessionsHeld"/> and
    /// <paramref name="sessionsUnseen"/> are 1 and 0 for an intraday trade; <paramref name="holdEnd"/> is the exit instant unless the position counts as open longer (D11).
    /// </summary>
    public static Result<WalkOutcome> Close(
        in Position position, ExitReason reason, long exitEpoch, decimal exit, in Minute minute, decimal low, decimal high, int examined, int zero, ExitFlags flags, int sessionsHeld, int sessionsUnseen, long holdEnd, LegState? legs = null)
    {
        var candidate = position.Candidate;
        var isBuy = position.IsBuy;
        var entry = position.Entry;
        var risk = position.Risk;
        var exitFilled = position.Slip.Exit(isBuy, reason, exit, minute.High - minute.Low);
        var sign = isBuy ? 1m : -1m;
        var rExact = sign * (exit - entry) / risk;
        var rFilled = sign * (exitFilled - position.EntryFilled) / risk;
        ValueList<Leg>? tradeLegs = null;
        if (legs is { Partials.Count: > 0 })
        {
            // P4-01, D10: R of a trade with legs is the sum over the exit legs of share × (exit − entry) / R0, from the exact and from the filled prices; the unit stays the risk of the first fill.
            var entryFilled = position.EntryFilled;
            var final = 1m;
            var exactParts = 0m;
            var filledParts = 0m;
            var list = new List<Leg>(legs.Partials.Count + 2) { new(position.FillEpoch, (double)entry, (double)entryFilled, 1m, LegKind.Entry, string.Empty) };
            foreach (var partial in legs.Partials)
            {
                final -= partial.Share;
                exactParts += partial.Share * (partial.Price - entry);
                filledParts += partial.Share * (partial.Filled - entryFilled);
                list.Add(new Leg(partial.Epoch, (double)partial.Price, (double)partial.Filled, partial.Share, LegKind.Exit, partial.Code));
            }

            rExact = sign * ((final * (exit - entry)) + exactParts) / risk;
            rFilled = sign * ((final * (exitFilled - entryFilled)) + filledParts) / risk;
            list.Add(new Leg(exitEpoch, (double)exit, (double)exitFilled, final, LegKind.Exit, reason.ToString()));
            tradeLegs = new ValueList<Leg>(list);
        }

        var adverse = isBuy ? (entry - low) / risk : (high - entry) / risk;
        var favourable = isBuy ? (high - entry) / risk : (entry - low) / risk;
        var trade = new SimTrade(
            SimTrade.CurrentSchemaVersion,
            candidate.StrategyId,
            candidate.Instrument.Id,
            candidate.Instrument.Symbol,
            candidate.Side,
            SessionClock.DateOf(position.FillEpoch),
            candidate.DecisionEpochSeconds,
            candidate.TriggerTimeframe,
            candidate.TriggerBar,
            candidate.TriggerBarMinutes,
            candidate.TriggerBarComplete,
            position.FillEpoch,
            (double)entry,
            (double)position.Stop,
            position.Target is { } target ? (double)target : null,
            exitEpoch,
            (double)exit,
            reason,
            D(rExact),
            D(adverse),
            D(favourable),
            position.Basis,
            CostBasis.None,
            EntrySnapshotJson: null,
            D(rFilled),
            D(rFilled - rExact),
            (double)position.EntryFilled,
            (double)exitFilled,
            position.EntryFill,
            position.TickFallback,
            sessionsHeld,
            sessionsUnseen,
            Legs: tradeLegs);

        return Result.Success(new WalkOutcome(
            trade, null, examined, position.AfterMissingMinutes, flags.StopGap, flags.TargetGap, flags.Ambiguous, flags.LateSquareOff, position.RiskUnderFourTicks, zero,
            flags.MaxHoldAtLastClose, flags.StopGap && flags.Overnight, flags.TargetGap && flags.Overnight, holdEnd, position.OvernightEntry, tradeLegs?.Count ?? 0, legs?.StopLoosenIgnored ?? 0, flags.TimeStopAtLastClose));
    }

    /// <summary>A decimal figure as the double of a <see cref="SimTrade"/> field. A decimal zero can carry a sign (<c>-1m * 0m</c>), and a double <c>-0</c> is printed as "-0" in a golden file or in JSON: zero is always +0.</summary>
    private static double D(decimal value) => value == 0m ? 0d : (double)value;

    public static bool TryRead(Bar bar, out Minute minute)
    {
        if (TickGrid.TryConvert(bar.Open, out var open) && TickGrid.TryConvert(bar.High, out var high) && TickGrid.TryConvert(bar.Low, out var low))
        {
            minute = new Minute(open, high, low);
            return true;
        }

        minute = default;
        return false;
    }

    public static Result<WalkOutcome> NoTrade(NoTradeReason reason, int examined, int zeroMinutes) =>
        Result.Success(new WalkOutcome(null, reason, examined, EntryAfterMissingMinutes: false, StopGapExit: false, TargetGapExit: false, AmbiguousMinute: false, LateSquareOff: false, RiskUnderFourTicks: false, zeroMinutes));

    public static Result<WalkOutcome> NotAscending() =>
        Result.Failure<WalkOutcome>(Error.Validation(SimulationErrors.BarsNotAscendingCode, "The minutes of the walk are not strictly ascending: two have the same start or one is earlier than the one before."));

    public static Result<WalkOutcome> PriceInvalid() =>
        Result.Failure<WalkOutcome>(Error.Validation("simulation.bar_price_invalid", "A minute the walk visits has a price that is not a finite number."));
}
