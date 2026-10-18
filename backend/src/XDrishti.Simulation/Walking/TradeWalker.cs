using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The second stage of the simulator (ADR 0037): an entry candidate and the 1-minute bars that follow it become a <see cref="SimTrade"/>, or the reason there is none. A pure function of its three arguments. It knows no
/// feature frame, no compiler and no clock, reads minutes strictly forward from the fill minute and stops at the exit, so a closed trade cannot depend on the minutes after it.
/// <para>
/// <b>Entry (D3).</b> The fill minute is <c>bars[DecisionBarIndex + 1]</c>; the entry price is its open. No such minute, one on another India date or one that starts more than
/// <see cref="SimulationEngine.MaxFillDelayMinutes"/> after the decision instant is <see cref="NoTradeReason.NoFillBar"/>; one that starts at or after the square-off is <see cref="NoTradeReason.AfterSquareOff"/>; a fill at or
/// through the stop is <see cref="NoTradeReason.EntryThroughStop"/>.
/// <b>Exit (D4).</b> From the fill minute on, minute by minute on the entry date: a minute at or after the square-off exits at its open; a touched stop exits at the stop, or at the open when the market gapped through it
/// (stop first: a minute that holds the stop and the target is a stop); a touched target exits at the target; the end of the entry date's data exits at the last close.
/// <b>R, MAE, MFE (D5).</b> In units of the initial risk <c>|entry − stop|</c>; the exit minute's range is counted as D5 says.
/// </para>
/// <para>
/// Failures that are not an outcome are data: <c>simulation.rules_invalid</c> (a stop level that is not finite, a reward-to-risk that is not above 0, a square-off minute outside 1 to 1439), <c>simulation.candidate_not_in_bars</c> and <c>simulation.bars_not_ascending</c> (two equal epochs or a descending pair among the minutes the walk
/// visits; the whole list is checked once by the caller, never per candidate).
/// </para>
/// </summary>
public static class TradeWalker
{
    private const long SecondsPerMinute = 60;

    /// <summary>Walks one candidate over the 1-minute bars of its instrument (strictly ascending).</summary>
    /// <param name="candidate">The decision to enter.</param>
    /// <param name="bars">The 1-minute bars of the candidate's instrument; <c>bars[candidate.DecisionBarIndex]</c> is the minute that closed the evaluation bar.</param>
    /// <param name="rules">The reward-to-risk and the square-off minute.</param>
    public static Result<WalkOutcome> Walk(EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkRules rules)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(bars);

        if (!double.IsFinite(candidate.StopLevel))
        {
            return Result.Failure<WalkOutcome>(Error.Validation("simulation.rules_invalid", "The stop level is not a finite number."));
        }

        if (rules.RewardRisk is { } rewardRisk && !(double.IsFinite(rewardRisk) && rewardRisk > 0))
        {
            return Result.Failure<WalkOutcome>(Error.Validation("simulation.rules_invalid", "The reward-to-risk must be a finite number above 0."));
        }

        if (rules.SquareOffMinute is < 1 or > 1439)
        {
            return Result.Failure<WalkOutcome>(Error.Validation("simulation.rules_invalid", "The square-off minute must be a minute of the India day, from 1 to 1439 (915 is 15:15)."));
        }

        var index = candidate.DecisionBarIndex;
        if ((uint)index >= (uint)bars.Count || bars[index].EpochSeconds != candidate.DecisionEpochSeconds - SecondsPerMinute)
        {
            return Result.Failure<WalkOutcome>(Error.Validation("simulation.candidate_not_in_bars", "The candidate's minute is not at its index in the list of bars, or does not end at the decision instant."));
        }

        var fillIndex = index + 1;
        if (fillIndex >= bars.Count)
        {
            return NoTrade(NoTradeReason.NoFillBar, 0);
        }

        var own = bars[index];
        var fill = bars[fillIndex];
        if (fill.EpochSeconds <= own.EpochSeconds)
        {
            return NotAscending();
        }

        if (SessionClock.DateOf(fill.EpochSeconds) != SessionClock.DateOf(own.EpochSeconds)
            || fill.EpochSeconds - candidate.DecisionEpochSeconds > SimulationEngine.MaxFillDelayMinutes * SecondsPerMinute)
        {
            return NoTrade(NoTradeReason.NoFillBar, 0);
        }

        if (SessionClock.MinuteOfDay(fill.EpochSeconds) >= rules.SquareOffMinute)
        {
            return NoTrade(NoTradeReason.AfterSquareOff, 0);
        }

        var isBuy = candidate.Side == Side.Buy;
        var entry = fill.Open;
        var stop = candidate.StopLevel;
        if (isBuy ? entry <= stop : entry >= stop)
        {
            return NoTrade(NoTradeReason.EntryThroughStop, 1);
        }

        var risk = Math.Abs(entry - stop);
        double? target = rules.RewardRisk is { } multiple ? (isBuy ? entry + (multiple * risk) : entry - (multiple * risk)) : null;
        var position = new Position(candidate, fill, entry, stop, target, risk, fill.EpochSeconds > candidate.DecisionEpochSeconds);

        return Follow(position, bars, fillIndex, rules.SquareOffMinute);
    }

    /// <summary>Visits the minutes from the fill minute on until the position leaves.</summary>
    private static Result<WalkOutcome> Follow(in Position position, IReadOnlyList<Bar> bars, int fillIndex, int squareOffMinute)
    {
        var isBuy = position.Candidate.Side == Side.Buy;
        var entryDate = SessionClock.DateOf(position.Fill.EpochSeconds);
        var low = position.Entry;
        var high = position.Entry;
        var last = fillIndex;
        for (var i = fillIndex; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (i > fillIndex)
            {
                if (bar.EpochSeconds <= bars[i - 1].EpochSeconds)
                {
                    return NotAscending();
                }

                if (SessionClock.DateOf(bar.EpochSeconds) != entryDate)
                {
                    break;
                }
            }

            if (SessionClock.MinuteOfDay(bar.EpochSeconds) >= squareOffMinute)
            {
                return Close(position, ExitReason.SquareOff, bar.EpochSeconds, bar.Open, Math.Min(low, bar.Open), Math.Max(high, bar.Open), i - fillIndex + 1, stopGap: false, targetGap: false);
            }

            if (isBuy ? bar.Low <= position.Stop : bar.High >= position.Stop)
            {
                var exit = isBuy ? Math.Min(position.Stop, bar.Open) : Math.Max(position.Stop, bar.Open);
                var gap = isBuy ? bar.Open < position.Stop : bar.Open > position.Stop;
                return Close(position, ExitReason.Stop, bar.EpochSeconds, exit, Math.Min(low, exit), Math.Max(high, exit), i - fillIndex + 1, gap, targetGap: false);
            }

            if (position.Target is { } target && (isBuy ? bar.High >= target : bar.Low <= target))
            {
                var gap = isBuy ? bar.Open > target : bar.Open < target;

                // D5: the favourable side of the target minute is the target distance (no improvement beyond it); its adverse side counts in full.
                var targetLow = isBuy ? Math.Min(low, bar.Low) : Math.Min(low, target);
                var targetHigh = isBuy ? Math.Max(high, target) : Math.Max(high, bar.High);
                return Close(position, ExitReason.Target, bar.EpochSeconds, target, targetLow, targetHigh, i - fillIndex + 1, stopGap: false, gap);
            }

            low = Math.Min(low, bar.Low);
            high = Math.Max(high, bar.High);
            last = i;
        }

        var lastBar = bars[last];
        return Close(position, ExitReason.EndOfData, lastBar.EpochSeconds, lastBar.Close, low, high, last - fillIndex + 1, stopGap: false, targetGap: false);
    }

    /// <summary>
    /// Builds the trade. <paramref name="low"/> and <paramref name="high"/> are the extreme prices that count for the excursions (D5), already restricted to what the exit minute may contribute.
    /// </summary>
    private static Result<WalkOutcome> Close(in Position position, ExitReason reason, long exitEpoch, double exitPrice, double low, double high, int examined, bool stopGap, bool targetGap)
    {
        var candidate = position.Candidate;
        var isBuy = candidate.Side == Side.Buy;
        var entry = position.Entry;
        var risk = position.Risk;
        var result = isBuy ? (exitPrice - entry) / risk : (entry - exitPrice) / risk;
        var adverse = isBuy ? (entry - low) / risk : (high - entry) / risk;
        var favourable = isBuy ? (high - entry) / risk : (entry - low) / risk;
        var trade = new SimTrade(
            SimTrade.CurrentSchemaVersion,
            candidate.StrategyId,
            candidate.Instrument.Id,
            candidate.Instrument.Symbol,
            candidate.Side,
            SessionClock.DateOf(position.Fill.EpochSeconds),
            candidate.DecisionEpochSeconds,
            candidate.TriggerTimeframe,
            candidate.TriggerBar,
            candidate.TriggerBarMinutes,
            candidate.TriggerBarComplete,
            position.Fill.EpochSeconds,
            entry,
            position.Stop,
            position.Target,
            exitEpoch,
            exitPrice,
            reason,
            result,
            adverse,
            favourable,
            FillBasis.Exact,
            CostBasis.None,
            EntrySnapshotJson: null);

        return Result.Success(new WalkOutcome(trade, null, examined, position.AfterMissingMinutes, stopGap, targetGap));
    }

    private static Result<WalkOutcome> NoTrade(NoTradeReason reason, int examined) =>
        Result.Success(new WalkOutcome(null, reason, examined, EntryAfterMissingMinutes: false, StopGapExit: false, TargetGapExit: false));

    private static Result<WalkOutcome> NotAscending() =>
        Result.Failure<WalkOutcome>(Error.Validation("simulation.bars_not_ascending", "The minutes of the walk are not strictly ascending: two have the same start or one is earlier than the one before."));

    /// <summary>What the loop needs to know about the entered position; a stack value, nothing per minute.</summary>
    private readonly record struct Position(EntryCandidate Candidate, Bar Fill, double Entry, double Stop, double? Target, double Risk, bool AfterMissingMinutes);
}
