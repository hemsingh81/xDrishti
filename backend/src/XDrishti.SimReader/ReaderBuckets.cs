namespace XDrishti.SimReader;

/// <summary>One evaluation bar built from the 1-minute bars present, anchored at 09:15. <see cref="Index"/> is its place in the session grid, <see cref="Volume"/> the sum of its minutes.</summary>
internal sealed record Bucket(int Index, long StartEpoch, int FirstBar, int LastBar, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>Evaluation bars of one session and the step at which each closes.</summary>
internal static class ReaderBuckets
{
    /// <summary>Buckets of <paramref name="minutes"/> anchored at 09:15, built from the minutes present.</summary>
    public static List<Bucket> Build(List<ReaderBar> bars, int minutes)
    {
        var buckets = new List<Bucket>();
        for (var i = 0; i < bars.Count; i++)
        {
            var offset = ReaderClock.MinuteOfDay(bars[i]) - ReaderClock.SessionOpen;
            var index = offset / minutes;
            if (buckets.Count > 0 && buckets[^1].Index == index)
            {
                var b = buckets[^1];
                buckets[^1] = b with { LastBar = i, High = Math.Max(b.High, bars[i].High), Low = Math.Min(b.Low, bars[i].Low), Close = bars[i].Close, Volume = b.Volume + bars[i].Volume };
            }
            else
            {
                var start = bars[i].EpochSeconds - ((offset % minutes) * ReaderClock.SecondsPerMinute);
                buckets.Add(new Bucket(index, start, i, i, bars[i].Open, bars[i].High, bars[i].Low, bars[i].Close, bars[i].Volume));
            }
        }

        return buckets;
    }

    /// <summary>
    /// A bucket closes at the first minute present that starts at or after the last minute of its span; one minute closing several buckets decides the last.
    /// <paramref name="oneDecisionPerMinute"/> merges buckets that close at the same minute into the last (a decision rule); a series of bars (the hourly trend) keeps every closed bucket. Returns (bucket position, index of the closing minute) in order.
    /// </summary>
    public static List<(int Bucket, int Closing)> Closes(List<ReaderBar> bars, List<Bucket> buckets, int minutes, bool oneDecisionPerMinute = true)
    {
        var closes = new List<(int Bucket, int Closing)>();
        for (var b = 0; b < buckets.Count; b++)
        {
            // a partial last bucket (the 15:15 hourly stub) closes at the step of the 15:29 minute
            var lastMinute = Math.Min(ReaderClock.SessionOpen + ((buckets[b].Index + 1) * minutes) - 1, ReaderClock.SessionClose - 1);
            var closing = -1;
            for (var j = buckets[b].LastBar; j < bars.Count; j++)
            {
                if (ReaderClock.MinuteOfDay(bars[j]) >= lastMinute)
                {
                    closing = j;
                    break;
                }
            }

            if (closing < 0)
            {
                continue;
            }

            if (oneDecisionPerMinute && closes.Count > 0 && closes[^1].Closing == closing)
            {
                closes[^1] = (b, closing);
            }
            else
            {
                closes.Add((b, closing));
            }
        }

        return closes;
    }
}
