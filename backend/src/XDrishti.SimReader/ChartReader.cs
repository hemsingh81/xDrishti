namespace XDrishti.SimReader;

/// <summary>
/// Reads one ORB trade off one India trading day of 1-minute bars, from the protocol (section 4 step 1) and the rule tables of ADR 0037 and 0038 only.
/// Pure, decimal, no clock. Returns null when the rules give no trade.
/// </summary>
public static class ChartReader
{
    public static ReaderReading? Read(IReadOnlyList<ReaderBar> oneMinuteBars, ReaderStrategy strategy, ReaderSide side)
    {
        ArgumentNullException.ThrowIfNull(oneMinuteBars);
        ArgumentNullException.ThrowIfNull(strategy);
        Validate(strategy);

        var bars = ReaderClock.SessionBars(oneMinuteBars);
        var orEnd = ReaderClock.SessionOpen + strategy.OrMinutes;

        // the opening range: the minutes present that start inside [09:15, 09:15 + or_min)
        var orHigh = decimal.MinValue;
        var orLow = decimal.MaxValue;
        var orCount = 0;
        foreach (var bar in bars)
        {
            if (ReaderClock.MinuteOfDay(bar) < orEnd)
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
        var buy = side == ReaderSide.Buy;
        var fill = ReaderWalk.IntradayFill(bars, buy, triggerBucket.Close, closingIndex, stop, strategy.Tick, strategy.EntryLimitPercent, ReaderClock.Minutes(strategy.SquareOff));
        if (fill is null)
        {
            return null;
        }

        var (fillIndex, entry, atLimit) = fill.Value;
        var risk = Math.Abs(entry - stop);
        var direction = side == ReaderSide.Buy ? 1m : -1m;
        var target = entry + direction * strategy.RewardRisk * risk;
        var (exitIndex, exitPrice, reason) = ReaderWalk.Exit(bars, buy, fillIndex, atLimit, stop, target, ReaderClock.Minutes(strategy.SquareOff), null);
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

    private static (Bucket Trigger, int ClosingIndex)? FindTrigger(List<ReaderBar> bars, ReaderStrategy s, ReaderSide side, decimal orHigh, decimal orLow, int orEnd)
    {
        var buckets = ReaderBuckets.Build(bars, s.EvaluationMinutes);
        var closes = ReaderBuckets.Closes(bars, buckets, s.EvaluationMinutes);

        foreach (var (b, closing) in closes)
        {
            var decisionMinute = ReaderClock.MinuteOfDay(bars[closing]) + 1;
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
}
