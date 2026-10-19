namespace XDrishti.SimReader;

/// <summary>
/// Reads one ORB trade off one India trading day of 1-minute bars, from the protocol (section 4 step 1) and the rule tables of ADR 0037 and 0038 only.
/// Pure, decimal, no clock. Returns null when the rules give no trade.
/// </summary>
public static class ChartReader
{
    private const int SecondsPerMinute = 60;
    private const int IndiaOffsetSeconds = 19800;
    private const int SessionOpen = 9 * 60 + 15;
    private const int SessionClose = 15 * 60 + 30;
    private const int MaxFillDelaySeconds = 5 * 60;

    public static ReaderReading? Read(IReadOnlyList<ReaderBar> oneMinuteBars, ReaderStrategy strategy, ReaderSide side)
    {
        ArgumentNullException.ThrowIfNull(oneMinuteBars);
        ArgumentNullException.ThrowIfNull(strategy);
        Validate(strategy);

        var bars = SessionBars(oneMinuteBars);
        var orEnd = SessionOpen + strategy.OrMinutes;

        // the opening range: the minutes present that start inside [09:15, 09:15 + or_min)
        var orHigh = decimal.MinValue;
        var orLow = decimal.MaxValue;
        var orCount = 0;
        foreach (var bar in bars)
        {
            if (MinuteOfDay(bar) < orEnd)
            {
                orHigh = Math.Max(orHigh, bar.High);
                orLow = Math.Min(orLow, bar.Low);
                orCount++;
            }
        }

        if (orCount == 0)
        {
            return null;
        }

        var candidate = FindTrigger(bars, strategy, side, orHigh, orLow, orEnd);
        if (candidate is null)
        {
            return null;
        }

        var (triggerBucket, closingIndex) = candidate.Value;
        var stop = side == ReaderSide.Buy ? orLow : orHigh;
        var fill = Fill(bars, strategy, side, triggerBucket.Close, closingIndex, stop);
        if (fill is null)
        {
            return null;
        }

        var (fillIndex, entry, atLimit) = fill.Value;
        var risk = Math.Abs(entry - stop);
        var direction = side == ReaderSide.Buy ? 1m : -1m;
        var target = entry + direction * strategy.RewardRisk * risk;
        var (exitIndex, exitPrice, reason) = Exit(bars, strategy, side, fillIndex, atLimit, stop, target);
        var seenR = Math.Round(direction * (exitPrice - entry) / risk, 6);

        return new ReaderReading(
            orHigh,
            orLow,
            triggerBucket.StartEpoch,
            triggerBucket.Close,
            bars[fillIndex].EpochSeconds,
            entry,
            atLimit,
            stop,
            target,
            bars[exitIndex].EpochSeconds,
            exitPrice,
            reason,
            seenR);
    }

    private static void Validate(ReaderStrategy s)
    {
        if (s.EvaluationMinutes <= 0 || s.OrMinutes <= 0 || s.OrMinutes % s.EvaluationMinutes != 0)
        {
            throw new ArgumentException("or_min must be a whole number of evaluation bars.", nameof(s));
        }

        if (s.Tick <= 0m || s.RewardRisk <= 0m || s.EntryLimitPercent < 0m)
        {
            throw new ArgumentException("tick and reward-risk must be above 0 and the limit percent not negative.", nameof(s));
        }
    }

    private static int MinuteOfDay(ReaderBar bar) => (int)(((bar.EpochSeconds + IndiaOffsetSeconds) % 86400) / SecondsPerMinute);

    private static long DayOf(ReaderBar bar) => (bar.EpochSeconds + IndiaOffsetSeconds) / 86400;

    /// <summary>The caller's list must be one India date in ascending order; minutes outside 09:15 to 15:30 are not part of the session and are left out.</summary>
    private static List<ReaderBar> SessionBars(IReadOnlyList<ReaderBar> input)
    {
        var result = new List<ReaderBar>(input.Count);
        for (var i = 0; i < input.Count; i++)
        {
            if (DayOf(input[i]) != DayOf(input[0]))
            {
                throw new ArgumentException("The bars must be of one India date.", nameof(input));
            }

            if (i > 0 && input[i].EpochSeconds <= input[i - 1].EpochSeconds)
            {
                throw new ArgumentException("The bars must be strictly ascending.", nameof(input));
            }

            var minute = MinuteOfDay(input[i]);
            if (minute >= SessionOpen && minute < SessionClose)
            {
                result.Add(input[i]);
            }
        }

        return result;
    }

    private sealed record Bucket(int Index, long StartEpoch, int FirstBar, int LastBar, decimal Close);

    private static (Bucket Trigger, int ClosingIndex)? FindTrigger(List<ReaderBar> bars, ReaderStrategy s, ReaderSide side, decimal orHigh, decimal orLow, int orEnd)
    {
        // evaluation bars: buckets of EvaluationMinutes anchored at 09:15, built from the minutes present
        var buckets = new List<Bucket>();
        for (var i = 0; i < bars.Count; i++)
        {
            var index = (MinuteOfDay(bars[i]) - SessionOpen) / s.EvaluationMinutes;
            if (buckets.Count > 0 && buckets[^1].Index == index)
            {
                buckets[^1] = buckets[^1] with { LastBar = i, Close = bars[i].Close };
            }
            else
            {
                var startEpoch = bars[i].EpochSeconds - ((MinuteOfDay(bars[i]) - SessionOpen) % s.EvaluationMinutes) * SecondsPerMinute;
                buckets.Add(new Bucket(index, startEpoch, i, i, bars[i].Close));
            }
        }

        // a bucket closes at the first minute present that starts at or after the last minute of its span; one minute closing several buckets decides the last
        var closes = new List<(int Bucket, int Closing)>();
        for (var b = 0; b < buckets.Count; b++)
        {
            var lastMinute = SessionOpen + ((buckets[b].Index + 1) * s.EvaluationMinutes) - 1;
            var closing = -1;
            for (var j = buckets[b].LastBar; j < bars.Count; j++)
            {
                if (MinuteOfDay(bars[j]) >= lastMinute)
                {
                    closing = j;
                    break;
                }
            }

            if (closing >= 0)
            {
                if (closes.Count > 0 && closes[^1].Closing == closing)
                {
                    closes[^1] = (b, closing);
                }
                else
                {
                    closes.Add((b, closing));
                }
            }
        }

        foreach (var (b, closing) in closes)
        {
            var decisionMinute = MinuteOfDay(bars[closing]) + 1;
            if (decisionMinute < orEnd
                || decisionMinute < (s.FirstEntry.Hour * 60) + s.FirstEntry.Minute
                || decisionMinute > (s.LastEntry.Hour * 60) + s.LastEntry.Minute)
            {
                continue;
            }

            if (b == 0)
            {
                continue; // close[1] is unknown on the first bar of the day: no break can be true there
            }

            var close = buckets[b].Close;
            var previous = buckets[b - 1].Close;
            var broke = side == ReaderSide.Buy
                ? close > orHigh && previous <= orHigh
                : close < orLow && previous >= orLow;
            if (broke)
            {
                return (buckets[b], closing);
            }
        }

        return null;
    }

    private static decimal TickDown(decimal price, decimal tick) => Math.Floor(price / tick) * tick;

    private static decimal TickUp(decimal price, decimal tick) => Math.Ceiling(price / tick) * tick;

    private static (int Index, decimal Price, bool AtLimit)? Fill(List<ReaderBar> bars, ReaderStrategy s, ReaderSide side, decimal triggerClose, int closingIndex, decimal stop)
    {
        var decisionInstant = bars[closingIndex].EpochSeconds + SecondsPerMinute;
        var fillIndex = -1;
        for (var i = closingIndex + 1; i < bars.Count; i++)
        {
            if (bars[i].Volume > 0m)
            {
                fillIndex = i;
                break;
            }
        }

        if (fillIndex < 0 || bars[fillIndex].EpochSeconds - decisionInstant > MaxFillDelaySeconds)
        {
            return null;
        }

        var squareOffMinute = (s.SquareOff.Hour * 60) + s.SquareOff.Minute;
        if (MinuteOfDay(bars[fillIndex]) >= squareOffMinute)
        {
            return null;
        }

        var p = s.EntryLimitPercent / 100m;
        var bar = bars[fillIndex];
        decimal entry;
        bool atLimit;
        if (side == ReaderSide.Buy)
        {
            var limit = TickDown(triggerClose * (1m + p), s.Tick);
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

            if (entry <= stop)
            {
                return null;
            }
        }
        else
        {
            var limit = TickUp(triggerClose * (1m - p), s.Tick);
            if (bar.Open >= limit)
            {
                (entry, atLimit) = (bar.Open, false);
            }
            else if (bar.High >= limit)
            {
                (entry, atLimit) = (limit, true);
            }
            else
            {
                return null;
            }

            if (entry >= stop)
            {
                return null;
            }
        }

        return (fillIndex, entry, atLimit);
    }

    private static (int Index, decimal Price, ReaderExitReason Reason) Exit(List<ReaderBar> bars, ReaderStrategy s, ReaderSide side, int fillIndex, bool atLimit, decimal stop, decimal target)
    {
        var buy = side == ReaderSide.Buy;
        var squareOffMinute = (s.SquareOff.Hour * 60) + s.SquareOff.Minute;
        var lastTrade = fillIndex;
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
            if (MinuteOfDay(bar) >= squareOffMinute)
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
        }

        return (lastTrade, bars[lastTrade].Close, ReaderExitReason.EndOfData);
    }
}
