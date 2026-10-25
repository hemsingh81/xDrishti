namespace XDrishti.Application.Abstractions.Reporting;

/// <summary>
/// The filters a store can apply to the trades of the runs it reads (P8-06, P8-00 D4); an empty list is no filter on that column. The values are already checked by the query parser (codes and ids, never free text), and a store passes them as parameters.
/// The filters on the labels of a run (style, exit) are applied to the set of runs before any trade is read.
/// </summary>
/// <param name="Strategies">Strategy ids.</param>
/// <param name="Timeframes">Timeframe codes (<c>15m</c>).</param>
/// <param name="Sides">BUY or SELL.</param>
/// <param name="Symbols">Instrument symbols.</param>
/// <param name="Baskets">Basket ids the trade belongs to (as of its run); a trade matches when it is in at least one.</param>
/// <param name="From">First India date of the entry, inclusive.</param>
/// <param name="To">Last India date of the entry, inclusive.</param>
public sealed record ReportTradeFilter(
    IReadOnlyList<string> Strategies,
    IReadOnlyList<string> Timeframes,
    IReadOnlyList<string> Sides,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<Guid> Baskets,
    DateOnly? From,
    DateOnly? To)
{
    /// <summary>No filter at all.</summary>
    public static ReportTradeFilter None { get; } = new([], [], [], [], [], null, null);

    /// <summary>True when no column is filtered.</summary>
    public bool IsEmpty => Strategies.Count == 0 && Timeframes.Count == 0 && Sides.Count == 0 && Symbols.Count == 0 && Baskets.Count == 0 && From is null && To is null;
}
