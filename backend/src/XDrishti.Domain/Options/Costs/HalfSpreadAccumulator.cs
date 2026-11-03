using XDrishti.Domain.Market;

namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// Collects the half-spreads of usable chain sessions, one session at a time, and turns them into the buckets of the model (spec P14-07). A quote counts when it is two-sided and uncrossed; its half-spread is <c>(ask - bid) / 2 / mid</c> in percent, mid being
/// <c>(ask + bid) / 2</c>. The ATM of a snapshot (one instant and expiry) is the strike nearest the underlying's price (the lower on a tie) and a step is the smallest gap between the snapshot's strikes. A snapshot with fewer than two strikes has no step and
/// is skipped, as is a quote outside every bucket; both are counted. A bucket with fewer than the least quotes uses its (moneyness, days to expiry) pool with the time-of-day split merged, then the days-to-expiry pool with the moneyness split merged too;
/// still short, it has no row, which is how <c>unavailable</c> is stored. Pure.
/// </summary>
public sealed class HalfSpreadAccumulator
{
    private readonly List<double>[] _cells = [.. Enumerable.Range(0, SpreadBuckets.Moneyness.Count * SpreadBuckets.Dte.Count * SpreadBuckets.Tod.Count).Select(_ => new List<double>())];

    /// <summary>Quotes that were two-sided, uncrossed and inside a bucket.</summary>
    public long Used { get; private set; }

    /// <summary>Quotes left out: one-sided, crossed, outside every bucket or in a snapshot with no strike step.</summary>
    public long Skipped { get; private set; }

    /// <summary>Adds the rows of one usable session.</summary>
    public void AddSession(DateOnly session, IReadOnlyList<SpreadQuote> quotes)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        foreach (var snapshot in quotes.GroupBy(q => (q.Ts, q.Expiry)))
        {
            var rows = snapshot.ToList();
            var strikes = rows.Select(r => r.StrikePx).Distinct().Order().ToList();
            if (strikes.Count < 2)
            {
                Skipped += rows.Count;
                continue;
            }

            var step = strikes.Zip(strikes.Skip(1), (a, b) => b - a).Min();
            var underlying = rows[0].UnderlyingPx;
            var atm = strikes.MinBy(s => Math.Abs((long)s - underlying));
            var days = snapshot.Key.Expiry.DayNumber - session.DayNumber;
            var time = TimeOnly.FromDateTime(IndiaTime.ToIst(snapshot.Key.Ts).DateTime);
            foreach (var row in rows)
            {
                var steps = (int)Math.Round((double)Math.Abs((long)row.StrikePx - atm) / step);
                if (!row.TwoSided || SpreadBuckets.Of(steps, days, time) is not { } bucket)
                {
                    Skipped++;
                    continue;
                }

                _cells[Index(bucket.Moneyness, bucket.Dte, bucket.Tod)].Add((row.AskPx!.Value - row.BidPx!.Value) * 100.0 / (row.AskPx.Value + row.BidPx.Value));
                Used++;
            }
        }
    }

    /// <summary>The rows of the model for the least quotes per bucket: the buckets, the merged-time pools and the merged days-to-expiry pools that reach it.</summary>
    public IReadOnlyList<SpreadBucketRow> Rows(int minQuotesPerBucket)
    {
        var rows = new List<SpreadBucketRow>();
        foreach (var d in SpreadBuckets.Dte)
        {
            var pooledDte = new List<double>();
            foreach (var m in SpreadBuckets.Moneyness)
            {
                var pooledMoneyness = new List<double>();
                foreach (var t in SpreadBuckets.Tod)
                {
                    var cell = _cells[Index(m, d, t)];
                    pooledMoneyness.AddRange(cell);
                    Add(rows, m, d, t, cell, minQuotesPerBucket);
                }

                pooledDte.AddRange(pooledMoneyness);
                Add(rows, m, d, SpreadBuckets.All, pooledMoneyness, minQuotesPerBucket);
            }

            Add(rows, SpreadBuckets.All, d, SpreadBuckets.All, pooledDte, minQuotesPerBucket);
        }

        return rows;
    }

    /// <summary>The median of the values (the mean of the two middle ones for an even count). The list is sorted in place.</summary>
    public static double Median(List<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0;
    }

    private static void Add(List<SpreadBucketRow> rows, string moneyness, string dte, string tod, List<double> values, int minQuotes)
    {
        if (values.Count > 0 && values.Count >= minQuotes)
        {
            rows.Add(new SpreadBucketRow(moneyness, dte, tod, values.Count, Median([.. values])));
        }
    }

    private static int Index(string moneyness, string dte, string tod) =>
        (SpreadBuckets.MoneynessIndex(moneyness) * SpreadBuckets.Dte.Count + SpreadBuckets.DteIndex(dte)) * SpreadBuckets.Tod.Count + SpreadBuckets.TodIndex(tod);
}
