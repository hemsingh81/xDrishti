using XDrishti.Application.Abstractions.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>The filters of a report query after the whitelist (P8-00 D4): empty is no filter. Style and exit are labels of a run and narrow the set of runs; the rest narrow the trades.</summary>
/// <param name="Strategies">Strategy ids.</param>
/// <param name="Timeframes">Timeframe codes.</param>
/// <param name="Sides">BUY or SELL.</param>
/// <param name="Styles">Run style labels.</param>
/// <param name="Exits">Run exit labels.</param>
/// <param name="Baskets">Basket ids.</param>
/// <param name="Symbols">Symbols.</param>
/// <param name="From">First entry date, inclusive.</param>
/// <param name="To">Last entry date, inclusive.</param>
public sealed record ReportFilters(
    IReadOnlyList<string> Strategies,
    IReadOnlyList<string> Timeframes,
    IReadOnlyList<string> Sides,
    IReadOnlyList<string> Styles,
    IReadOnlyList<string> Exits,
    IReadOnlyList<Guid> Baskets,
    IReadOnlyList<string> Symbols,
    DateOnly? From,
    DateOnly? To)
{
    /// <summary>No filter.</summary>
    public static ReportFilters None { get; } = new([], [], [], [], [], [], [], null, null);

    /// <summary>The part the store applies to the trades.</summary>
    public ReportTradeFilter ToTradeFilter() => new(Strategies, Timeframes, Sides, Symbols, Baskets, From, To);

    /// <summary>True when a run's labels pass the style and exit filters (a run that records none of a filtered label does not pass it).</summary>
    /// <param name="info">The run.</param>
    public bool KeepsRun(ReportRunInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return (Styles.Count == 0 || (info.Style is not null && Styles.Contains(info.Style, StringComparer.Ordinal)))
            && (Exits.Count == 0 || (info.Exit is not null && Exits.Contains(info.Exit, StringComparer.Ordinal)));
    }
}
