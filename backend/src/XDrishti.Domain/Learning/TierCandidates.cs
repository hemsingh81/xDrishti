using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>The traded value (close x volume, rupees) of one share on one session.</summary>
/// <param name="Date">The India session date.</param>
/// <param name="Value">Close x volume, not negative.</param>
public readonly record struct SessionValue(DateOnly Date, decimal Value);

/// <summary>
/// The candidates of one decision date from daily traded values (BK-11, criterion 4). Only sessions strictly before the date are read, so appending later bars changes no earlier date's candidates (the truncation property). A share with no session before
/// the date is not a candidate (it is not in the dated set that day): its trades are <see cref="UniverseTier.Unknown"/>. Pure.
/// </summary>
public static class TierCandidates
{
    /// <param name="series">Each share's sessions, oldest first.</param>
    /// <param name="date">The decision date.</param>
    public static IReadOnlyList<TierCandidate> At(IReadOnlyDictionary<Guid, IReadOnlyList<SessionValue>> series, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(series);
        var result = new List<TierCandidate>(series.Count);
        foreach (var (id, sessions) in series)
        {
            int lo = 0, hi = sessions.Count;   // the count of sessions strictly before the date, by binary search (the list is oldest first)
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (sessions[mid].Date < date)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            var before = lo;

            if (before == 0)
            {
                continue;
            }

            var start = Math.Max(0, before - LiquidityTiers.MedianSessions);
            var window = new List<decimal>(before - start);
            for (var i = start; i < before; i++)
            {
                window.Add(sessions[i].Value);
            }

            result.Add(new TierCandidate(id, before, LiquidityTiers.Median(window)));
        }

        return result;
    }
}
