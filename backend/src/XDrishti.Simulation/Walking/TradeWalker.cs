using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The second stage of the simulator (ADR 0037, ADR 0038): an entry candidate and the 1-minute bars that follow it become a <see cref="SimTrade"/>, or the reason there is none. A pure function of its three arguments. It
/// knows no feature frame, no compiler and no clock, reads minutes strictly forward from the fill minute and stops at the exit, so a closed trade cannot depend on the minutes after it.
/// <para>
/// <b>Entry (E0 to E6).</b> A minute with volume 0 holds no trade and is passed over. The entry is a limit order placed at the decision instant at the trigger close plus the entry limit percent, rounded down (a sell: up) to
/// the tick; it lives for the first minute that holds a trade at or after the decision instant, at most <see cref="SimulationEngine.MaxFillDelayMinutes"/> after it and before the square-off. It fills at that minute's open when the
/// open is within the limit, at the limit when the minute trades back to it, and is otherwise <see cref="NoTradeReason.LimitNotReached"/>; a fill at or through the stop is <see cref="NoTradeReason.EntryThroughStop"/>.
/// <b>Exit (X1 to X6).</b> Minute by minute on the entry date: the first minute that holds a trade at or after the square-off minute leaves at its open (a stop or target already passed at that open is what fills); a touched stop
/// leaves at the stop, or at the open when the minute gapped through it; a touched target leaves at the target and never at a better price; a minute that reaches both is a stop unless its open is already beyond the target; in the
/// fill minute of an entry at the limit only the stop is checked; the end of the entry date's data leaves at the last close.
/// <b>Slippage (S1 to S4).</b> Market-type fills (the entry at the open, the stop, the square-off and the end of the data) pay <c>(Ticks + extra) × tick + BarRangeFrac × range</c> against the trade and are rounded on the tick grid;
/// a resting limit does not slip. Decisions use the exact prices, so slippage never changes a decision.
/// <b>R, MAE, MFE.</b> In units of the initial risk <c>|entry − stop|</c> of the exact entry; <c>RFilled</c> uses the same unit. Everything is computed in decimal.
/// </para>
/// <para>
/// Failures that are not an outcome are data: <c>simulation.fill_rules_invalid</c> and <c>simulation.tick_size_invalid</c> (a <see cref="WalkRules"/> that bypassed the factories), <c>simulation.rules_invalid</c> (a stop level or
/// trigger close that is not a finite number, a reward-to-risk that is not above 0, a square-off minute outside 1 to 1439), <c>simulation.candidate_not_in_bars</c>, <c>simulation.bars_not_ascending</c> (two equal epochs or a
/// descending pair among the minutes the walk visits; the whole list is checked once by the caller, never per candidate) and <c>simulation.bar_price_invalid</c> (a visited minute whose price is not a finite number).
/// </para>
/// </summary>
public static class TradeWalker
{
    private const long SecondsPerMinute = 60;

    private const decimal PercentToFraction = 100m;

    private const int RiskFloorTicks = 4;

    /// <summary>Walks one candidate over the 1-minute bars of its instrument (strictly ascending).</summary>
    /// <param name="candidate">The decision to enter.</param>
    /// <param name="bars">The 1-minute bars of the candidate's instrument; <c>bars[candidate.DecisionBarIndex]</c> is the minute that closed the evaluation bar.</param>
    /// <param name="rules">The reward-to-risk, the square-off minute, the fill rules and the tick.</param>
    public static Result<WalkOutcome> Walk(EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkRules rules)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(bars);

        if (rules.Fills is null)
        {
            return Result.Failure<WalkOutcome>(SimulationErrors.FillRulesInvalid(nameof(WalkRules.Fills), "is required"));
        }

        if (rules.Tick is null)
        {
            return Result.Failure<WalkOutcome>(SimulationErrors.TickSizeInvalid("the tick is required"));
        }

        if (!TickGrid.TryConvert(candidate.StopLevel, out var stop))
        {
            return Result.Failure<WalkOutcome>(Error.Validation("simulation.rules_invalid", "The stop level is not a finite number."));
        }

        if (!TickGrid.TryConvert(candidate.TriggerBar.Close, out var triggerClose))
        {
            return Result.Failure<WalkOutcome>(Error.Validation("simulation.rules_invalid", "The close of the trigger bar is not a finite number."));
        }

        decimal? multiple = null;
        if (rules.RewardRisk is { } rewardRisk)
        {
            if (!(rewardRisk > 0 && TickGrid.TryConvert(rewardRisk, out var converted) && converted > 0))
            {
                return Result.Failure<WalkOutcome>(Error.Validation("simulation.rules_invalid", "The reward-to-risk must be a finite number above 0."));
            }

            multiple = converted;
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

        // E1: the fill minute is the first minute after the candidate's own that holds a trade (E0), on its date and at most five minutes after the decision instant.
        var entryDate = SessionClock.DateOf(bars[index].EpochSeconds);
        var previousEpoch = bars[index].EpochSeconds;
        var zeroMinutes = 0;
        var fillIndex = -1;
        for (var j = index + 1; j < bars.Count; j++)
        {
            var bar = bars[j];
            if (bar.EpochSeconds <= previousEpoch)
            {
                return NotAscending();
            }

            previousEpoch = bar.EpochSeconds;
            if (SessionClock.DateOf(bar.EpochSeconds) != entryDate || bar.EpochSeconds - candidate.DecisionEpochSeconds > SimulationEngine.MaxFillDelayMinutes * SecondsPerMinute)
            {
                break;
            }

            if (bar.Volume <= 0)
            {
                zeroMinutes++;
                continue;
            }

            fillIndex = j;
            break;
        }

        if (fillIndex < 0)
        {
            return NoTrade(NoTradeReason.NoFillBar, 0, zeroMinutes);
        }

        var fillBar = bars[fillIndex];
        if (SessionClock.MinuteOfDay(fillBar.EpochSeconds) >= rules.SquareOffMinute)
        {
            return NoTrade(NoTradeReason.AfterSquareOff, 0, zeroMinutes);
        }

        if (!TryRead(fillBar, out var fill))
        {
            return PriceInvalid();
        }

        // E2: the limit is an order price, rounded on the grid in both fill modes so that it never exceeds the limit percent.
        var isBuy = candidate.Side == Side.Buy;
        var tick = rules.Tick.Size;
        _ = TickGrid.TryConvert(rules.Fills.EntryLimitPercent, out var percent);
        var fraction = percent / PercentToFraction;
        var limit = isBuy ? TickGrid.Down(triggerClose * (1m + fraction), tick) : TickGrid.Up(triggerClose * (1m - fraction), tick);

        // E3 to E5: at the open, at the limit, or missed.
        decimal entry;
        EntryFill kind;
        if (isBuy ? fill.Open <= limit : fill.Open >= limit)
        {
            entry = fill.Open;
            kind = EntryFill.AtOpen;
        }
        else if (isBuy ? fill.Low <= limit : fill.High >= limit)
        {
            entry = limit;
            kind = EntryFill.AtLimit;
        }
        else
        {
            return NoTrade(NoTradeReason.LimitNotReached, 1, zeroMinutes);
        }

        // E6: a fill at or through the stop is no trade.
        if (Beyond(isBuy, entry, stop))
        {
            return NoTrade(NoTradeReason.EntryThroughStop, 1, zeroMinutes);
        }

        var risk = Math.Abs(entry - stop);
        decimal? target = multiple is { } k ? (isBuy ? entry + (k * risk) : entry - (k * risk)) : null;
        var slip = Slip.Of(rules.Fills.Slippage, tick);
        var entryFilled = kind == EntryFill.AtOpen ? slip.Entry(isBuy, entry, limit, fill.High - fill.Low) : entry;
        var position = new Position(
            candidate, fillBar.EpochSeconds, isBuy, entry, entryFilled, kind, stop, target, risk, slip,
            fillBar.EpochSeconds > candidate.DecisionEpochSeconds, risk < RiskFloorTicks * tick, rules.Fills.Basis, rules.Tick.FromFallback);

        return Follow(position, bars, fillIndex, rules.SquareOffMinute, entryDate, zeroMinutes);
    }

    /// <summary>Visits the minutes from the fill minute on until the position leaves.</summary>
    private static Result<WalkOutcome> Follow(in Position position, IReadOnlyList<Bar> bars, int fillIndex, int squareOffMinute, DateOnly entryDate, int zeroBefore)
    {
        var isBuy = position.IsBuy;
        var stop = position.Stop;
        var hasTarget = position.Target.HasValue;
        var target = position.Target.GetValueOrDefault();

        // The extremes that count for the excursions (D5 with the additions of ADR 0038): the adverse one is the low of a buy and the high of a sell.
        var low = position.Entry;
        var high = position.Entry;
        var examined = 0;
        var zero = zeroBefore;
        var zeroAtLast = zeroBefore;
        var last = fillIndex;
        var previousEpoch = bars[fillIndex].EpochSeconds;
        for (var i = fillIndex; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (i > fillIndex)
            {
                if (bar.EpochSeconds <= previousEpoch)
                {
                    return NotAscending();
                }

                previousEpoch = bar.EpochSeconds;
                if (SessionClock.DateOf(bar.EpochSeconds) != entryDate)
                {
                    break;
                }

                if (bar.Volume <= 0)
                {
                    zero++;
                    continue;
                }
            }

            examined++;
            if (!TryRead(bar, out var minute))
            {
                return PriceInvalid();
            }

            var minuteOfDay = SessionClock.MinuteOfDay(bar.EpochSeconds);
            var isFill = i == fillIndex;

            // X1: the first minute that holds a trade at or after the square-off closes the position at its open; its range is not used.
            if (!isFill && minuteOfDay >= squareOffMinute)
            {
                if (Beyond(isBuy, minute.Open, stop))
                {
                    var (stopLow, stopHigh) = Worsen(isBuy, low, high, minute.Open);
                    return Close(position, ExitReason.Stop, bar.EpochSeconds, minute.Open, minute, stopLow, stopHigh, examined, zero, new Flags(StopGap: isBuy ? minute.Open < stop : minute.Open > stop));
                }

                if (hasTarget && Reaches(isBuy, minute.Open, target))
                {
                    var (targetLow, targetHigh) = Raise(isBuy, low, high, target);
                    return Close(position, ExitReason.Target, bar.EpochSeconds, target, minute, targetLow, targetHigh, examined, zero, new Flags(TargetGap: isBuy ? minute.Open > target : minute.Open < target));
                }

                return Close(
                    position, ExitReason.SquareOff, bar.EpochSeconds, minute.Open, minute, Math.Min(low, minute.Open), Math.Max(high, minute.Open), examined, zero, new Flags(LateSquareOff: minuteOfDay > squareOffMinute));
            }

            // X5: in the fill minute of an entry at the limit only the stop is checked; the favourable extreme of that minute may have come before the fill.
            var atLimitFill = isFill && position.EntryFill == EntryFill.AtLimit;
            var stopHit = Beyond(isBuy, isBuy ? minute.Low : minute.High, stop);
            var targetHit = hasTarget && !atLimitFill && Reaches(isBuy, isBuy ? minute.High : minute.Low, target);
            var openBeyondTarget = hasTarget && Reaches(isBuy, minute.Open, target);
            var targetGap = hasTarget && (isBuy ? minute.Open > target : minute.Open < target);

            if (targetHit && openBeyondTarget)
            {
                // X3 and the exception of X4: the first print is beyond the target, so the target fills there, whatever the minute did next; the minute contributes no range.
                var (targetLow, targetHigh) = Raise(isBuy, low, high, target);
                return Close(position, ExitReason.Target, bar.EpochSeconds, target, minute, targetLow, targetHigh, examined, zero, new Flags(TargetGap: targetGap));
            }

            if (stopHit)
            {
                // X2 and X4: the stop, or the open when the market gapped through it; a minute that reaches both is this row, and ambiguous when the open lies strictly between them.
                var exit = isBuy ? Math.Min(stop, minute.Open) : Math.Max(stop, minute.Open);
                var (stopLow, stopHigh) = Worsen(isBuy, low, high, exit);
                var flags = new Flags(StopGap: isBuy ? minute.Open < stop : minute.Open > stop, Ambiguous: targetHit && !Beyond(isBuy, minute.Open, stop));
                return Close(position, ExitReason.Stop, bar.EpochSeconds, exit, minute, stopLow, stopHigh, examined, zero, flags);
            }

            if (targetHit)
            {
                // X3: the target, never a better price; the adverse side of the minute counts in full.
                var (targetLow, targetHigh) = Raise(isBuy, low, high, target);
                var (adverseLow, adverseHigh) = Worsen(isBuy, targetLow, targetHigh, isBuy ? minute.Low : minute.High);
                return Close(position, ExitReason.Target, bar.EpochSeconds, target, minute, adverseLow, adverseHigh, examined, zero, new Flags(TargetGap: targetGap));
            }

            (low, high) = atLimitFill ? Worsen(isBuy, low, high, isBuy ? minute.Low : minute.High) : (Math.Min(low, minute.Low), Math.Max(high, minute.High));
            last = i;
            zeroAtLast = zero;
        }

        // X6: no minute that holds a trade at or after the square-off: the close of the last minute of the date that does.
        var lastBar = bars[last];
        if (!TryRead(lastBar, out var lastMinute) || !TickGrid.TryConvert(lastBar.Close, out var close))
        {
            return PriceInvalid();
        }

        return Close(position, ExitReason.EndOfData, lastBar.EpochSeconds, close, lastMinute, low, high, examined, zeroAtLast, default);
    }

    /// <summary>
    /// Builds the trade. <paramref name="low"/> and <paramref name="high"/> are the extreme prices that count for the excursions, already restricted to what the exit minute may contribute.
    /// </summary>
    private static Result<WalkOutcome> Close(in Position position, ExitReason reason, long exitEpoch, decimal exit, in Minute minute, decimal low, decimal high, int examined, int zero, Flags flags)
    {
        var candidate = position.Candidate;
        var isBuy = position.IsBuy;
        var entry = position.Entry;
        var risk = position.Risk;
        var exitFilled = position.Slip.Exit(isBuy, reason, exit, minute.High - minute.Low);
        var sign = isBuy ? 1m : -1m;
        var rExact = sign * (exit - entry) / risk;
        var rFilled = sign * (exitFilled - position.EntryFilled) / risk;
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
            (double)rExact,
            (double)adverse,
            (double)favourable,
            position.Basis,
            CostBasis.None,
            EntrySnapshotJson: null,
            (double)rFilled,
            (double)(rFilled - rExact),
            (double)position.EntryFilled,
            (double)exitFilled,
            position.EntryFill,
            position.TickFallback);

        return Result.Success(new WalkOutcome(trade, null, examined, position.AfterMissingMinutes, flags.StopGap, flags.TargetGap, flags.Ambiguous, flags.LateSquareOff, position.RiskUnderFourTicks, zero));
    }

    /// <summary>The extremes after the adverse side reaches <paramref name="price"/>: the low of a buy, the high of a sell.</summary>
    private static (decimal Low, decimal High) Worsen(bool isBuy, decimal low, decimal high, decimal price) =>
        isBuy ? (Math.Min(low, price), high) : (low, Math.Max(high, price));

    /// <summary>The extremes after the favourable side reaches <paramref name="price"/>: the high of a buy, the low of a sell.</summary>
    private static (decimal Low, decimal High) Raise(bool isBuy, decimal low, decimal high, decimal price) =>
        isBuy ? (low, Math.Max(high, price)) : (Math.Min(low, price), high);

    /// <summary>A price at or beyond the stop (a buy: at or below it).</summary>
    private static bool Beyond(bool isBuy, decimal price, decimal stop) => isBuy ? price <= stop : price >= stop;

    /// <summary>A price at or beyond the target (a buy: at or above it).</summary>
    private static bool Reaches(bool isBuy, decimal price, decimal target) => isBuy ? price >= target : price <= target;

    private static bool TryRead(Bar bar, out Minute minute)
    {
        if (TickGrid.TryConvert(bar.Open, out var open) && TickGrid.TryConvert(bar.High, out var high) && TickGrid.TryConvert(bar.Low, out var low))
        {
            minute = new Minute(open, high, low);
            return true;
        }

        minute = default;
        return false;
    }

    private static Result<WalkOutcome> NoTrade(NoTradeReason reason, int examined, int zeroMinutes) =>
        Result.Success(new WalkOutcome(null, reason, examined, EntryAfterMissingMinutes: false, StopGapExit: false, TargetGapExit: false, AmbiguousMinute: false, LateSquareOff: false, RiskUnderFourTicks: false, zeroMinutes));

    private static Result<WalkOutcome> NotAscending() =>
        Result.Failure<WalkOutcome>(Error.Validation("simulation.bars_not_ascending", "The minutes of the walk are not strictly ascending: two have the same start or one is earlier than the one before."));

    private static Result<WalkOutcome> PriceInvalid() =>
        Result.Failure<WalkOutcome>(Error.Validation("simulation.bar_price_invalid", "A minute the walk visits has a price that is not a finite number."));

    /// <summary>The open, high and low of a visited minute as decimals.</summary>
    private readonly record struct Minute(decimal Open, decimal High, decimal Low);

    /// <summary>The facts about the exit that the counters sum.</summary>
    private readonly record struct Flags(bool StopGap = false, bool TargetGap = false, bool Ambiguous = false, bool LateSquareOff = false);

    /// <summary>What the loop needs to know about the entered position; a stack value, nothing per minute.</summary>
    private readonly record struct Position(
        EntryCandidate Candidate,
        long FillEpoch,
        bool IsBuy,
        decimal Entry,
        decimal EntryFilled,
        EntryFill EntryFill,
        decimal Stop,
        decimal? Target,
        decimal Risk,
        Slip Slip,
        bool AfterMissingMinutes,
        bool RiskUnderFourTicks,
        FillBasis Basis,
        bool TickFallback);

    /// <summary>S1 to S3: the slippage of market-type fills on the price grid of the instrument, in decimal.</summary>
    private readonly record struct Slip(bool Modelled, decimal Ticks, decimal Extra, decimal Fraction, decimal Tick)
    {
        public static Slip Of(SlippageConfig config, decimal tick)
        {
            _ = TickGrid.TryConvert(config.Ticks, out var ticks);
            _ = TickGrid.TryConvert(config.StopExitExtraTicks, out var extra);
            _ = TickGrid.TryConvert(config.BarRangeFrac, out var fraction);
            return new Slip(!config.IsNone, ticks, extra, fraction, tick);
        }

        /// <summary>S2: <c>(Ticks + extra) × tick + BarRangeFrac × range</c> of the minute the fill happens in; the extra ticks are for stop exits.</summary>
        private decimal Amount(decimal range, bool stopExit) => ((Ticks + (stopExit ? Extra : 0m)) * Tick) + (Fraction * range);

        /// <summary>S3 for an entry at the open: against the trade, on the grid, never past the limit. With no slippage the entry does not move.</summary>
        public decimal Entry(bool isBuy, decimal entry, decimal limit, decimal range)
        {
            if (!Modelled)
            {
                return entry;
            }

            var amount = Amount(range, stopExit: false);
            return isBuy ? Math.Min(TickGrid.Up(entry + amount, Tick), limit) : Math.Max(TickGrid.Down(entry - amount, Tick), limit);
        }

        /// <summary>S1 and S3 for an exit: a target is a resting limit and only goes on the grid; every other exit slips against the trade, a stop with the extra ticks. With no slippage nothing moves.</summary>
        public decimal Exit(bool isBuy, ExitReason reason, decimal exit, decimal range)
        {
            if (!Modelled)
            {
                return exit;
            }

            var amount = reason == ExitReason.Target ? 0m : Amount(range, stopExit: reason == ExitReason.Stop);
            return isBuy ? TickGrid.Down(exit - amount, Tick) : TickGrid.Up(exit + amount, Tick);
        }
    }
}
