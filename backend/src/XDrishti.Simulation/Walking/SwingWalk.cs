using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The swing loop (ADR 0040, X7 to X9): the minutes of as many sessions as it takes, with the rows X2 to X5 of <see cref="MinuteRule"/> for every minute that holds a trade on every date, no square-off, and three ways to end.
/// <list type="bullet">
/// <item><b>X8</b> — a minute of the due session that is its due minute and holds a trade, in which neither level was reached, leaves at its <b>close</b> (<see cref="ExitReason.MaxHold"/>); the stop and the target come first.</item>
/// <item><b>X8, second rule</b> — the first minute that is on a later date than the due session, or on the due session after its due minute, shows that the due minute held no trade: the position leaves at the close of the last minute
/// that holds a trade (<see cref="ExitFlags.MaxHoldAtLastClose"/>). Only the start of that minute is read.</item>
/// <item><b>X9</b> — the list ends first: the close of the last minute that holds a trade, <see cref="ExitReason.EndOfData"/>.</item>
/// </list>
/// The due session is found by the calendar alone (<see cref="SwingHold.DueSession"/>), never by what the list contains, so the hold cannot depend on the future.
/// </summary>
internal static class SwingWalk
{
    public static Result<WalkOutcome> Follow(in Position position, IReadOnlyList<Bar> bars, int fillIndex, SwingHold hold, DateOnly entryDate, DateOnly due, int zeroBefore, LegState? legs)
    {
        var dueMinute = hold.DueMinute(due);
        var low = position.Entry;
        var high = position.Entry;
        var examined = 0;
        var zero = zeroBefore;
        var zeroAtLast = zeroBefore;
        var last = fillIndex;
        var seen = 0;
        var seenDate = DateOnly.MinValue;
        var previousEpoch = bars[fillIndex].EpochSeconds;
        var pastDue = false;
        for (var i = fillIndex; i < bars.Count; i++)
        {
            var bar = bars[i];
            var date = entryDate;
            var minuteOfDay = SessionClock.MinuteOfDay(bar.EpochSeconds);
            if (i > fillIndex)
            {
                if (bar.EpochSeconds <= previousEpoch)
                {
                    return WalkResults.NotAscending();
                }

                previousEpoch = bar.EpochSeconds;
                date = SessionClock.DateOf(bar.EpochSeconds);
                if (date > due || (date == due && minuteOfDay > dueMinute))
                {
                    pastDue = true;
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

            var firstOfLaterDate = date != seenDate && date > entryDate;
            if (date != seenDate)
            {
                seen++;
                seenDate = date;
            }

            // P4-01: the levels as a rule has moved them, from the first minute after the close that produced them (D2, D6).
            if (legs is not null && i != fillIndex)
            {
                MinuteRule.AdvanceLegs(legs, minute, bar.EpochSeconds);
            }

            ref readonly var current = ref (legs is null ? ref position : ref legs.Current);
            if (legs is { SignalExit: true } && !MinuteRule.Beyond(position.IsBuy, minute.Open, current.Stop))
            {
                // P4-03, D7: an EMA signal leaves at this open as a market order, unless the open is already through the stop (the resting order comes first, X2).
                return Leave(position, hold, entryDate, seen, ExitReason.EmaExit, bar.EpochSeconds, minute.Open, minute, Math.Min(low, minute.Open), Math.Max(high, minute.Open), examined, zero, default, bar.EpochSeconds, legs);
            }

            var verdict = MinuteRule.Decide(current, minute, i == fillIndex, low, high);
            if (verdict.Exits)
            {
                var flags = verdict.Flags with { Overnight = firstOfLaterDate };
                return Leave(position, hold, entryDate, seen, verdict.Reason, bar.EpochSeconds, verdict.Exit, minute, verdict.Low, verdict.High, examined, zero, flags, bar.EpochSeconds, legs);
            }

            low = verdict.Low;
            high = verdict.High;
            legs?.Observe(minute);
            last = i;
            zeroAtLast = zero;
            if (date == due && minuteOfDay == dueMinute)
            {
                // X8: the due minute held a trade and neither level was reached: the close of the minute.
                if (!TickGrid.TryConvert(bar.Close, out var dueClose))
                {
                    return WalkResults.PriceInvalid();
                }

                var (dueLow, dueHigh) = MinuteRule.Raise(position.IsBuy, low, high, dueClose);
                return Leave(position, hold, entryDate, seen, ExitReason.MaxHold, bar.EpochSeconds, dueClose, minute, dueLow, dueHigh, examined, zero, default, bar.EpochSeconds, legs);
            }
        }

        // X8 (second rule) or X9: the close of the last minute that holds a trade.
        var lastBar = bars[last];
        if (!WalkResults.TryRead(lastBar, out var lastMinute) || !TickGrid.TryConvert(lastBar.Close, out var close))
        {
            return WalkResults.PriceInvalid();
        }

        // Q3-MFE (ISS-28, sim-6): the minute of the last close counts in full, so the favourable extreme reaches at least that close.
        var (endLow, endHigh) = MinuteRule.Raise(position.IsBuy, low, high, close);
        if (pastDue)
        {
            return Leave(position, hold, entryDate, seen, ExitReason.MaxHold, lastBar.EpochSeconds, close, lastMinute, endLow, endHigh, examined, zeroAtLast, new ExitFlags(MaxHoldAtLastClose: true), hold.SessionEnd(due), legs);
        }

        return Leave(position, hold, entryDate, seen, ExitReason.EndOfData, lastBar.EpochSeconds, close, lastMinute, endLow, endHigh, examined, zeroAtLast, default, lastBar.EpochSeconds, legs);
    }

    private static Result<WalkOutcome> Leave(
        in Position position, SwingHold hold, DateOnly entryDate, int seen, ExitReason reason, long exitEpoch, decimal exit, in Minute minute, decimal low, decimal high, int examined, int zero, ExitFlags flags, long holdEnd, LegState? legs)
    {
        var held = hold.SessionsBetween(entryDate, SessionClock.DateOf(exitEpoch));
        return WalkResults.Close(position, reason, exitEpoch, exit, minute, low, high, examined, zero, flags, held, Math.Max(0, held - seen), holdEnd, legs);
    }
}
