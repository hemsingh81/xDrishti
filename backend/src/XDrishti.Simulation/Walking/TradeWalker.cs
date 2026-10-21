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
/// <b>A swing walk</b> (<see cref="WalkRules.Hold"/> set; ADR 0040) has the same entry (on the decision's date) and the same per-minute rows X2 to X5 and S1 to S4 (<see cref="MinuteRule"/>, one routine for both loops), but no square-off (X7), goes
/// on over the dates of the position (<see cref="SwingWalk"/>), leaves with <see cref="ExitReason.MaxHold"/> at the close of the due session (X8) and ends with <see cref="ExitReason.EndOfData"/> only when the list of bars ends (X9).
/// </para>
/// <para>
/// <b>An order that rests overnight</b> (ADR 0043, E8 and E9). In a swing walk a decision at or after the end of its session (15:30, or the end of a special session) places its order for the next trading session by the calendar: it fills at the
/// first minute that holds a trade on that date and at most five minutes after the session's open, with the limit and the stop rows of the intraday entry (E2 to E6). A minute of another date that is not the fill session ends the search
/// (<see cref="NoTradeReason.NoFillBar"/>: a later session is never used); a list that has no minute after the candidate's own is <see cref="NoTradeReason.OrderPendingAtEnd"/>, because a longer list may fill the order. The due session is counted
/// from the fill session, the trade is dated on the fill date and <see cref="WalkOutcome.OvernightEntry"/> is set. An intraday walk never rests overnight.
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
    public static Result<WalkOutcome> Walk(EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkRules rules) => Walk(candidate, bars, rules, null);

    /// <summary>The walk of <see cref="Walk(EntryCandidate, IReadOnlyList{Bar}, WalkRules)"/> with an exit rule and its tape (P4-01); null is the <c>FIXED_RR</c> walk, whose rows and results are those of <c>sim-5</c> bit for bit.</summary>
    internal static Result<WalkOutcome> Walk(EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkRules rules, ExitContext? exit)
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
            return Result.Failure<WalkOutcome>(Error.Validation(SimulationErrors.RulesInvalidCode, "The stop level is not a finite number."));
        }

        if (!TickGrid.TryConvert(candidate.TriggerBar.Close, out var triggerClose))
        {
            return Result.Failure<WalkOutcome>(Error.Validation(SimulationErrors.RulesInvalidCode, "The close of the trigger bar is not a finite number."));
        }

        decimal? multiple = null;
        if (rules.RewardRisk is { } rewardRisk)
        {
            if (!(rewardRisk > 0 && TickGrid.TryConvert(rewardRisk, out var converted) && converted > 0))
            {
                return Result.Failure<WalkOutcome>(Error.Validation(SimulationErrors.RulesInvalidCode, "The reward-to-risk must be a finite number above 0."));
            }

            multiple = converted;
        }

        if (rules.Hold is null && rules.SquareOffMinute is < 1 or > 1439)
        {
            return Result.Failure<WalkOutcome>(Error.Validation(SimulationErrors.RulesInvalidCode, "The square-off minute must be a minute of the India day, from 1 to 1439 (915 is 15:15)."));
        }

        if (rules.Hold is not null && rules.SquareOffMinute != WalkRules.NoSquareOff)
        {
            return Result.Failure<WalkOutcome>(Error.Validation(SimulationErrors.RulesInvalidCode, "A swing walk has no square-off: the square-off minute must be WalkRules.NoSquareOff (0)."));
        }

        var index = candidate.DecisionBarIndex;
        if ((uint)index >= (uint)bars.Count || bars[index].EpochSeconds != candidate.DecisionEpochSeconds - SecondsPerMinute)
        {
            return Result.Failure<WalkOutcome>(Error.Validation(SimulationErrors.CandidateNotInBarsCode, "The candidate's minute is not at its index in the list of bars, or does not end at the decision instant."));
        }

        // E8: in a swing walk a decision at or after the end of its session places an order for the next session; an intraday walk never does.
        var decisionDate = SessionClock.DateOf(bars[index].EpochSeconds);
        var entryDate = decisionDate;
        var overnight = false;
        var fillOpen = 0L;
        if (rules.Hold is { } resting && SessionClock.MinuteOfDay(bars[index].EpochSeconds) + 1 >= resting.SessionEndMinute(decisionDate))
        {
            var next = resting.NextSession(decisionDate);
            if (next.IsFailure)
            {
                return Result.Failure<WalkOutcome>(next.Error);
            }

            overnight = true;
            entryDate = next.Value;
            fillOpen = resting.SessionOpen(entryDate);
        }

        // X8: the due session of a swing walk comes from the calendar alone, before any minute is read; the entry session (the fill session of an order resting overnight) is session 1.
        var due = default(DateOnly);
        if (rules.Hold is { } hold)
        {
            var found = hold.DueSession(entryDate);
            if (found.IsFailure)
            {
                return Result.Failure<WalkOutcome>(found.Error);
            }

            due = found.Value;
        }

        // E1 (E9 for an order resting overnight): the fill minute is the first minute after the candidate's own that holds a trade (E0), on the fill date and at most five minutes after the decision instant (after the open of the fill session).
        var previousEpoch = bars[index].EpochSeconds;
        var zeroMinutes = 0;
        var fillIndex = -1;
        var ranOff = true;   // the loop ran off the end of the list while the order could still fill
        for (var j = index + 1; j < bars.Count; j++)
        {
            var bar = bars[j];
            if (bar.EpochSeconds <= previousEpoch)
            {
                return WalkResults.NotAscending();
            }

            previousEpoch = bar.EpochSeconds;
            var barDate = SessionClock.DateOf(bar.EpochSeconds);
            if (overnight)
            {
                if (barDate == decisionDate)
                {
                    continue;
                }

                var delay = bar.EpochSeconds - fillOpen;
                if (barDate != entryDate || delay > SimulationEngine.MaxFillDelayMinutes * SecondsPerMinute)
                {
                    ranOff = false;
                    break;
                }

                if (delay < 0)
                {
                    continue;
                }
            }
            else if (barDate != entryDate || bar.EpochSeconds - candidate.DecisionEpochSeconds > SimulationEngine.MaxFillDelayMinutes * SecondsPerMinute)
            {
                ranOff = false;
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
            // D3: when the list ended while the order could still fill (no minute of another date, no minute past the five minutes), it cannot be said to have missed anything: a longer list may fill it.
            return WalkResults.NoTrade(overnight && ranOff ? NoTradeReason.OrderPendingAtEnd : NoTradeReason.NoFillBar, 0, zeroMinutes);
        }

        var fillBar = bars[fillIndex];
        if (rules.Hold is null && SessionClock.MinuteOfDay(fillBar.EpochSeconds) >= rules.SquareOffMinute)
        {
            return WalkResults.NoTrade(NoTradeReason.AfterSquareOff, 0, zeroMinutes);
        }

        if (!WalkResults.TryRead(fillBar, out var fill))
        {
            return WalkResults.PriceInvalid();
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
            return WalkResults.NoTrade(NoTradeReason.LimitNotReached, 1, zeroMinutes);
        }

        // E6: a fill at or through the stop is no trade.
        if (MinuteRule.Beyond(isBuy, entry, stop))
        {
            return WalkResults.NoTrade(NoTradeReason.EntryThroughStop, 1, zeroMinutes);
        }

        var risk = Math.Abs(entry - stop);
        decimal? target = multiple is { } k ? (isBuy ? entry + (k * risk) : entry - (k * risk)) : null;
        var slip = Slip.Of(rules.Fills.Slippage, tick);
        var entryFilled = kind == EntryFill.AtOpen ? slip.Entry(isBuy, entry, limit, fill.High - fill.Low) : entry;
        var position = new Position(
            candidate, fillBar.EpochSeconds, isBuy, entry, entryFilled, kind, stop, target, risk, slip,
            overnight ? fillBar.EpochSeconds > fillOpen : fillBar.EpochSeconds > candidate.DecisionEpochSeconds, risk < RiskFloorTicks * tick, rules.Fills.Basis, rules.Tick.FromFallback, overnight);

        var legs = exit is null ? null : new LegState(position, exit);
        return rules.Hold is { } swing
            ? SwingWalk.Follow(position, bars, fillIndex, swing, entryDate, due, zeroMinutes, legs)
            : Follow(position, bars, fillIndex, rules.SquareOffMinute, entryDate, zeroMinutes, legs);
    }

    /// <summary>Visits the minutes from the fill minute on until the position leaves (the intraday walk of ADR 0038: the entry date only, the square-off at <paramref name="squareOffMinute"/>).</summary>
    private static Result<WalkOutcome> Follow(in Position position, IReadOnlyList<Bar> bars, int fillIndex, int squareOffMinute, DateOnly entryDate, int zeroBefore, LegState? legs)
    {
        var isBuy = position.IsBuy;

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
                    return WalkResults.NotAscending();
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
            if (!WalkResults.TryRead(bar, out var minute))
            {
                return WalkResults.PriceInvalid();
            }

            var minuteOfDay = SessionClock.MinuteOfDay(bar.EpochSeconds);
            var isFill = i == fillIndex;

            // P4-01: the levels as a rule has moved them, from the first minute after the close that produced them (D2, D6); without a rule the position is the entry's.
            if (legs is not null && !isFill)
            {
                MinuteRule.AdvanceLegs(legs, minute, bar.EpochSeconds);
            }

            ref readonly var current = ref (legs is null ? ref position : ref legs.Current);
            var stop = current.Stop;
            var hasTarget = current.Target.HasValue;
            var target = current.Target.GetValueOrDefault();

            // X1: the first minute that holds a trade at or after the square-off closes the position at its open; its range is not used.
            if (!isFill && minuteOfDay >= squareOffMinute)
            {
                if (MinuteRule.Beyond(isBuy, minute.Open, stop))
                {
                    var (stopLow, stopHigh) = MinuteRule.Worsen(isBuy, low, high, minute.Open);
                    return Leave(position, ExitReason.Stop, bar.EpochSeconds, minute.Open, minute, stopLow, stopHigh, examined, zero, new ExitFlags(StopGap: isBuy ? minute.Open < stop : minute.Open > stop), legs);
                }

                if (hasTarget && MinuteRule.Reaches(isBuy, minute.Open, target))
                {
                    var (targetLow, targetHigh) = MinuteRule.Raise(isBuy, low, high, target);
                    return Leave(position, ExitReason.Target, bar.EpochSeconds, target, minute, targetLow, targetHigh, examined, zero, new ExitFlags(TargetGap: isBuy ? minute.Open > target : minute.Open < target), legs);
                }

                return Leave(
                    position, ExitReason.SquareOff, bar.EpochSeconds, minute.Open, minute, Math.Min(low, minute.Open), Math.Max(high, minute.Open), examined, zero, new ExitFlags(LateSquareOff: minuteOfDay > squareOffMinute), legs);
            }

            // P4-03, D7: an EMA signal leaves at this open as a market order, unless the open is already through the stop (the stop is a resting order and comes first, X2).
            if (legs is { SignalExit: true } && !MinuteRule.Beyond(isBuy, minute.Open, stop))
            {
                return Leave(position, ExitReason.EmaExit, bar.EpochSeconds, minute.Open, minute, Math.Min(low, minute.Open), Math.Max(high, minute.Open), examined, zero, default, legs);
            }

            // X2 to X5: the rows of the exit table, written once (MinuteRule) and shared with the swing loop.
            var verdict = MinuteRule.Decide(current, minute, isFill, low, high);
            if (verdict.Exits)
            {
                return Leave(position, verdict.Reason, bar.EpochSeconds, verdict.Exit, minute, verdict.Low, verdict.High, examined, zero, verdict.Flags, legs);
            }

            low = verdict.Low;
            high = verdict.High;
            legs?.Observe(minute);
            last = i;
            zeroAtLast = zero;
        }

        // X6: no minute that holds a trade at or after the square-off: the close of the last minute of the date that does.
        var lastBar = bars[last];
        if (!WalkResults.TryRead(lastBar, out var lastMinute) || !TickGrid.TryConvert(lastBar.Close, out var close))
        {
            return WalkResults.PriceInvalid();
        }

        // Q3-MFE (ISS-28, sim-6): the minute of the last close counts in full, so the favourable extreme reaches at least that close (it matters only in the fill minute of an entry at the limit, X5).
        var (endLow, endHigh) = MinuteRule.Raise(isBuy, low, high, close);
        return Leave(position, ExitReason.EndOfData, lastBar.EpochSeconds, close, lastMinute, endLow, endHigh, examined, zeroAtLast, default, legs);
    }

    /// <summary>An intraday exit: one session held, none unseen, the position open until the exit instant.</summary>
    private static Result<WalkOutcome> Leave(in Position position, ExitReason reason, long exitEpoch, decimal exit, in Minute minute, decimal low, decimal high, int examined, int zero, ExitFlags flags, LegState? legs) =>
        WalkResults.Close(position, reason, exitEpoch, exit, minute, low, high, examined, zero, flags, sessionsHeld: 1, sessionsUnseen: 0, holdEnd: exitEpoch, legs);
}
