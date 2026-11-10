using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Domain.Baskets.Derived;

/// <summary>One stay of an instrument in a list basket in India dates: a member from <see cref="From"/> on, no longer from <see cref="To"/> on (null = still a member).</summary>
public readonly record struct ListStay(DateOnly From, DateOnly? To)
{
    public bool Covers(DateOnly date) => From <= date && (To is null || date < To);
}

/// <summary>
/// The point-in-time flags of one instrument for basket 13 (BK-05), from the dated list stays of the ASM/GSM, ban and SME/trade-for-trade lists: one <see cref="DayFlag"/> at <c>knownSince</c> and one at every later date a stay begins or ends,
/// so the engine's "latest flag on or before the date" is the state of that date. There is none before <c>knownSince</c> (the first date all three lists were recorded): a missing flag is not a pass. Pure.
/// </summary>
public static class DayFlagBuilder
{
    public static DayFlag[] Build(DateOnly knownSince, string? masterSeries, IReadOnlyList<ListStay> asmGsm, IReadOnlyList<ListStay> ban, IReadOnlyList<ListStay> tradeForTrade)
    {
        ArgumentNullException.ThrowIfNull(asmGsm);
        ArgumentNullException.ThrowIfNull(ban);
        ArgumentNullException.ThrowIfNull(tradeForTrade);
        var dates = new SortedSet<DateOnly> { knownSince };
        foreach (var stay in asmGsm.Concat(ban).Concat(tradeForTrade))
        {
            if (stay.From > knownSince)
            {
                dates.Add(stay.From);
            }

            if (stay.To is { } to && to > knownSince)
            {
                dates.Add(to);
            }
        }

        return [.. dates.Select(d => new DayFlag(d, asmGsm.Any(s => s.Covers(d)), ban.Any(s => s.Covers(d)), SeriesOn(d, masterSeries, tradeForTrade)))];
    }

    // On a trade-for-trade date the series is not EQ; otherwise a share the master lists in one of those series was EQ before it moved there (the list says when).
    private static string SeriesOn(DateOnly date, string? masterSeries, IReadOnlyList<ListStay> tradeForTrade)
    {
        var series = masterSeries?.Trim().ToUpperInvariant();
        var restricted = series is not null && FlagListBuilder.TradeForTradeSeries.Contains(series);
        if (tradeForTrade.Any(s => s.Covers(date)))
        {
            return restricted ? series! : "BE";
        }

        return series is null ? string.Empty : restricted ? "EQ" : series;
    }
}
