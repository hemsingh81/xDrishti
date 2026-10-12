using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// Aggregates against the 1-minute bars: the stored buckets from the bounds on are compared, exactly, with the buckets <see cref="MinuteAggregator"/> recomputes from the 1-minute bars (open, high, low, close,
/// volume and bar count). A stored bucket the bars do not give, or a bucket the bars give that is not stored, is a difference too — the usual cause is aggregates not refreshed after late data.
/// </summary>
public static class AggregateChecker
{
    /// <summary>The most differing buckets that are kept in the result's details.</summary>
    public const int MaxDifferences = 200;

    /// <summary>Every bucket of the window, stored and recomputed, ordered by timeframe then time.</summary>
    public static IReadOnlyList<BucketComparison> Compare(IReadOnlyList<Bar> bars, IReadOnlyList<StoredBucket> stored, QualityBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(stored);
        var computed = MinuteAggregator.Build(bars);
        var storedByKey = new Dictionary<(Timeframe, long), StoredBucket>();
        foreach (var bucket in stored)
        {
            if (InRange(bucket.Timeframe, bucket.Start, bounds))
            {
                storedByKey[(bucket.Timeframe, bucket.Start)] = bucket;
            }
        }

        var keys = new HashSet<(Timeframe, long)>(storedByKey.Keys);
        foreach (var key in computed.Keys)
        {
            if (InRange(key.Timeframe, key.Start, bounds))
            {
                keys.Add(key);
            }
        }

        var rows = new List<BucketComparison>(keys.Count);
        foreach (var key in keys.OrderBy(k => Array.IndexOf(TimeframeOrder, k.Item1)).ThenBy(k => k.Item2))
        {
            StoredBucket? have = storedByKey.TryGetValue(key, out var s) ? s : null;
            AggBucket? want = computed.TryGetValue(key, out var c) ? c : null;
            rows.Add(new BucketComparison(key.Item1, key.Item2, have, want, Fields(have, want)));
        }

        return rows;
    }

    /// <summary>The check and the capped list of differing buckets, from a comparison.</summary>
    public static (AggregatesCheck Check, IReadOnlyList<BucketDifference> Differences) Summarize(IReadOnlyList<BucketComparison> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var byTimeframe = new Dictionary<string, TimeframeCheck>(StringComparer.Ordinal);
        foreach (var timeframe in Timeframes.Aggregated)
        {
            var own = rows.Where(r => r.Timeframe == timeframe).ToList();
            var differences = own.Count(r => r.Differs);
            byTimeframe[timeframe.ToCode()] = new TimeframeCheck(differences == 0 ? CheckStatus.Pass : CheckStatus.Fail, own.Count, differences);
        }

        var differing = rows.Where(r => r.Differs).ToList();
        var check = new AggregatesCheck(differing.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail, rows.Count, differing.Count, byTimeframe);
        return (check, [.. differing.Take(MaxDifferences).Select(r => new BucketDifference(r.Timeframe.ToCode(), r.Ts, r.Fields))]);
    }

    private static readonly Timeframe[] TimeframeOrder = [.. Timeframes.Aggregated];

    private static bool InRange(Timeframe timeframe, long start, QualityBounds bounds) => timeframe switch
    {
        Timeframe.W1 => start >= bounds.Week,
        Timeframe.Mo1 => start >= bounds.Month,
        _ => start >= bounds.Window,
    };

    private static string[] Fields(StoredBucket? have, AggBucket? want)
    {
        if (have is null)
        {
            return ["missing"];
        }

        if (want is null)
        {
            return ["unexpected"];
        }

        var a = have.Value;
        var b = want.Value;
        var fields = new List<string>(6);
        AddIf(fields, a.Open != b.Open, "open");
        AddIf(fields, a.High != b.High, "high");
        AddIf(fields, a.Low != b.Low, "low");
        AddIf(fields, a.Close != b.Close, "close");
        AddIf(fields, a.Volume != b.Volume, "volume");
        AddIf(fields, a.Bars != b.Bars, "bars");
        return [.. fields];
    }

    private static void AddIf(List<string> fields, bool condition, string name)
    {
        if (condition)
        {
            fields.Add(name);
        }
    }
}
