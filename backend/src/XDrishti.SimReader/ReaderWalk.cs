namespace XDrishti.SimReader;

/// <summary>The fill and exit rules of ADR 0038 (E2 to E6, X1 to X5) and ADR 0040 (X8) on 1-minute bars, shared by the readers. Gross: no slippage, no costs.</summary>
internal static class ReaderWalk
{
    public static decimal TickDown(decimal price, decimal tick) => Math.Floor(price / tick) * tick;

    public static decimal TickUp(decimal price, decimal tick) => Math.Ceiling(price / tick) * tick;

    /// <summary>The fill of the limit order on the fill minute: the open if inside the limit, the limit if the minute touches it, otherwise null; null too when the entry would not be on the right side of the stop.</summary>
    public static (decimal Price, bool AtLimit)? LimitFill(ReaderBar bar, bool buy, decimal triggerClose, decimal tick, decimal limitPercent, decimal stop)
    {
        var p = limitPercent / 100m;
        decimal entry;
        bool atLimit;
        if (buy)
        {
            var limit = TickDown(triggerClose * (1m + p), tick);
            if (bar.Open <= limit)
            {
                (entry, atLimit) = (bar.Open, false);
            }
            else if (bar.Low <= limit)
            {
                (entry, atLimit) = (limit, true);
            }
            else
            {
                return null;
            }

            return entry <= stop ? null : (entry, atLimit);
        }

        var sellLimit = TickUp(triggerClose * (1m - p), tick);
        if (bar.Open >= sellLimit)
        {
            (entry, atLimit) = (bar.Open, false);
        }
        else if (bar.High >= sellLimit)
        {
            (entry, atLimit) = (sellLimit, true);
        }
        else
        {
            return null;
        }

        return entry >= stop ? null : (entry, atLimit);
    }

    /// <summary>The first minute holding a trade after the closing minute, at most five minutes after the decision, before the square-off.</summary>
    public static (int Index, decimal Price, bool AtLimit)? IntradayFill(List<ReaderBar> bars, bool buy, decimal triggerClose, int closingIndex, decimal stop, decimal tick, decimal limitPercent, int squareOffMinute)
    {
        const int maxFillDelaySeconds = 5 * 60;
        var decisionInstant = bars[closingIndex].EpochSeconds + ReaderClock.SecondsPerMinute;
        var fillIndex = -1;
        for (var i = closingIndex + 1; i < bars.Count; i++)
        {
            if (bars[i].Volume > 0m)
            {
                fillIndex = i;
                break;
            }
        }

        if (fillIndex < 0 || bars[fillIndex].EpochSeconds - decisionInstant > maxFillDelaySeconds || ReaderClock.MinuteOfDay(bars[fillIndex]) >= squareOffMinute)
        {
            return null;
        }

        return LimitFill(bars[fillIndex], buy, triggerClose, tick, limitPercent, stop) is { } fill ? (fillIndex, fill.Price, fill.AtLimit) : null;
    }

    /// <summary>
    /// Walks the position from its fill minute. <paramref name="squareOffMinute"/> (intraday) closes at the open of the first trading minute at or after it;
    /// <paramref name="dueDay"/> (swing, the India day number of the last session of the hold) closes at the close of that day's last trading minute, after the stop and the target of that minute; a list that ends first is end_of_data.
    /// </summary>
    public static (int Index, decimal Price, ReaderExitReason Reason) Exit(List<ReaderBar> bars, bool buy, int fillIndex, bool atLimit, decimal stop, decimal target, int? squareOffMinute, long? dueDay)
    {
        var lastTrade = fillIndex;
        var dueIndex = -1;
        if (dueDay is { } due)
        {
            for (var i = fillIndex; i < bars.Count; i++)
            {
                if (bars[i].Volume > 0m && ReaderClock.DayOf(bars[i]) <= due)
                {
                    dueIndex = i;
                }
            }
        }

        for (var i = fillIndex; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (bar.Volume <= 0m)
            {
                continue; // a minute with volume 0 holds no trade
            }

            lastTrade = i;
            var openBeyondStop = buy ? bar.Open <= stop : bar.Open >= stop;
            var openBeyondTarget = buy ? bar.Open >= target : bar.Open <= target;
            if (squareOffMinute is { } squareOff && ReaderClock.MinuteOfDay(bar) >= squareOff)
            {
                // X1: the first minute holding a trade at or after the square-off closes the position at its open; its range is not used
                if (openBeyondStop)
                {
                    return (i, bar.Open, ReaderExitReason.Stop);
                }

                return openBeyondTarget ? (i, target, ReaderExitReason.Target) : (i, bar.Open, ReaderExitReason.SquareOff);
            }

            var stopTouched = buy ? bar.Low <= stop : bar.High >= stop;
            var targetTouched = !(i == fillIndex && atLimit) && (buy ? bar.High >= target : bar.Low <= target); // X5
            if (stopTouched && !(targetTouched && openBeyondTarget))
            {
                return (i, buy ? Math.Min(stop, bar.Open) : Math.Max(stop, bar.Open), ReaderExitReason.Stop);
            }

            if (targetTouched)
            {
                return (i, target, ReaderExitReason.Target);
            }

            if (i == dueIndex)
            {
                return (i, bar.Close, ReaderExitReason.MaxHold); // X8
            }
        }

        return (lastTrade, bars[lastTrade].Close, ReaderExitReason.EndOfData);
    }
}
