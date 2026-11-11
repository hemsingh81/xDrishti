using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Market.Strength;

/// <summary>
/// Sector strength (BK-09 <c>sector_rs</c>): the equal-weight mean of the members' window returns as of the day (equal weight at the start of the window, the buy-and-hold reading BK-06 uses for a hold), needing
/// <see cref="StrengthOptions.MinSectorMembers"/> members with a close on the day and on the session <c>window</c> sessions before it; ranks run among the sectors that have a value. Pure.
/// </summary>
public static class SectorStrength
{
    public static IReadOnlyList<SectorReturn> Compute(IReadOnlyDictionary<string, IReadOnlyList<MemberSeries>> sectors, DateOnly asOf, IReadOnlyList<int> windows, StrengthOptions options, IReadOnlyDictionary<string, IReadOnlyCollection<DateOnly>>? exDates = null)
    {
        ArgumentNullException.ThrowIfNull(sectors);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(options);
        var cleaned = sectors.OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => (Sector: s.Key, Series: s.Value.Select(m => (Symbol: m.Symbol, Clean: BasketStats.Clean(m.Closes, DateOnly.MinValue, asOf))).ToList()))
            .ToList();
        var result = new List<SectorReturn>();
        foreach (var window in windows)
        {
            var rows = new List<SectorReturn>();
            foreach (var (sector, series) in cleaned)
            {
                var returns = new List<double>();
                var excluded = 0;
                foreach (var (symbol, (dates, closes, _)) in series)
                {
                    if (dates.Length > window && dates[^1] == asOf)
                    {
                        if (exDates is not null && exDates.TryGetValue(symbol, out var ex) && ex.Any(x => x > dates[^(window + 1)] && x <= asOf))
                        {
                            excluded++;
                            continue;
                        }

                        returns.Add((closes[^1] / closes[^(window + 1)]) - 1.0);
                    }
                }

                rows.Add(returns.Count < options.MinSectorMembers
                    ? new SectorReturn(sector, window, Metric.Insufficient($"n = {returns.Count}, at least {options.MinSectorMembers} needed"), returns.Count, null, excluded)
                    : new SectorReturn(sector, window, Metric.Of(returns.Average()), returns.Count, null, excluded));
            }

            var values = rows.Where(r => r.Return.State == MetricState.Value).Select(r => r.Return.Number!.Value).ToList();
            result.AddRange(rows.Select(r => r.Return.State == MetricState.Value ? r with { Rank = 1 + values.Count(v => v > r.Return.Number!.Value) } : r));
        }

        return result;
    }
}
