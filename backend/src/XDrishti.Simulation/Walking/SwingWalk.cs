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
    public static Result<WalkOutcome> Follow(in Position position, IReadOnlyList<Bar> bars, int fillIndex, SwingHold hold, DateOnly entryDate, DateOnly due, int zeroBefore)
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

            var verdict = MinuteRule.Decide(position, minute, i == fillIndex, low, high);
            if (verdict.Exits)
            {
                var flags = verdict.Flags with { Overnight = firstOfLaterDate };
                return Leave(position, hold, entryDate, seen, verdict.Reason, bar.EpochSeconds, verdict.Exit, minute, verdict.Low, verdict.High, examined, zero, flags, bar.EpochSeconds);
            }

            low = verdict.Low;
            high = verdict.High;
            last = i;
            zeroAtLast = zero;
            if (date == due && minuteOfDay == dueMinute)
            {
                // X8: the due minute held a trade and neither level was reached: the close of the minute.
                if (!TickGrid.TryConvert(bar.Close, out var dueClose))
                {
                    return WalkResults.PriceInvalid();
                }

                return Leave(position, hold, entryDate, seen, ExitReason.MaxHold, bar.EpochSeconds, dueClose, minute, low, high, examined, zero, default, bar.EpochSeconds);
            }
        }

        // X8 (second rule) or X9: the close of the last minute that holds a trade.
        var lastBar = bars[last];
        if (!WalkResults.TryRead(lastBar, out var lastMinute) || !TickGrid.TryConvert(lastBar.Close, out var close))
        {
            return WalkResults.PriceInvalid();
        }

        if (pastDue)
        {
            return Leave(position, hold, entryDate, seen, ExitReason.MaxHold, lastBar.EpochSeconds, close, lastMinute, low, high, examined, zeroAtLast, new ExitFlags(MaxHoldAtLastClose: true), hold.SessionEnd(due));
        }

        return Leave(position, hold, entryDate, seen, ExitReason.EndOfData, lastBar.EpochSeconds, close, lastMinute, low, high, examined, zeroAtLast, default, lastBar.EpochSeconds);
    }

    private static Result<WalkOutcome> Leave(
        in Position position, SwingHold hold, DateOnly entryDate, int seen, ExitReason reason, long exitEpoch, decimal exit, in Minute minute, decimal low, decimal high, int examined, int zero, ExitFlags flags, long holdEnd)
    {
        var held = hold.SessionsBetween(entryDate, SessionClock.DateOf(exitEpoch));
        return WalkResults.Close(position, reason, exitEpoch, exit, minute, low, high, examined, zero, flags, held, Math.Max(0, held - seen), holdEnd);
    }
}
