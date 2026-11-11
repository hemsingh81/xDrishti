using XDrishti.Domain.Research;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// What a basket can absorb (BK-06 m14): ADV20 per member (the CF-05 median, <see cref="LiquidityTiers.Median"/>, of the last 20 daily traded values, close x volume), the basket median and 10th percentile, a capacity of a share of each ADV20 and the flag shares.
/// A member with fewer sessions than the window is listed, never guessed. Pure.
/// </summary>
public static class BasketCapacity
{
    public static BasketCapacityResult Compute(IReadOnlyList<MemberSeries> members, DateOnly asOf, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(options);
        var known = new List<(string Symbol, double Adv, bool IsFno, bool IsAsmGsm)>();
        var shortList = new List<string>();
        foreach (var m in members.OrderBy(x => x.Symbol, StringComparer.Ordinal))
        {
            var volumes = new Dictionary<DateOnly, double>();
            foreach (var v in m.Volumes)
            {
                if (v.Date <= asOf && double.IsFinite(v.Volume) && v.Volume >= 0)
                {
                    volumes[v.Date] = v.Volume;
                }
            }

            var (dates, closes, _) = BasketStats.Clean(m.Closes, DateOnly.MinValue, asOf);
            // newest first while collecting, oldest first for the CF-05 median
            var values = new List<decimal>();
            for (var i = dates.Length - 1; i >= 0 && values.Count < LiquidityTiers.MedianSessions; i--)
            {
                if (volumes.TryGetValue(dates[i], out var volume))
                {
                    values.Add((decimal)closes[i] * (decimal)volume);
                }
            }

            values.Reverse();
            if (LiquidityTiers.Median(values) is { } adv20)
            {
                known.Add((m.Symbol, (double)adv20, m.IsFno, m.IsAsmGsm));
            }
            else
            {
                shortList.Add(m.Symbol);
            }
        }

        return FromAdv(known, shortList, options.CapacityPct);
    }

    /// <summary>The basket figures from the per-member ADV20 (in the unit the caller uses).</summary>
    public static BasketCapacityResult FromAdv(IReadOnlyList<(string Symbol, double Adv, bool IsFno, bool IsAsmGsm)> members, IReadOnlyList<string> shortMembers, double capacityPct)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(shortMembers);
        var adv = members.ToDictionary(m => m.Symbol, m => m.Adv, StringComparer.Ordinal);
        if (members.Count == 0)
        {
            var none = Metric.Empty("no member has a full window of volume");
            return new BasketCapacityResult(none, none, none, none, none, 0, shortMembers, adv);
        }

        var sorted = members.Select(m => m.Adv).Order().ToArray();
        var capacity = sorted.Sum(a => a * capacityPct);
        return new BasketCapacityResult(
            Metric.Of(BasketStats.Quantile(sorted, 0.5)),
            Metric.Of(BasketStats.Quantile(sorted, 0.1)),
            Metric.Of(capacity),
            Metric.Of((double)members.Count(m => m.IsAsmGsm) / members.Count),
            Metric.Of((double)members.Count(m => m.IsFno) / members.Count),
            members.Count,
            shortMembers,
            adv);
    }
}
