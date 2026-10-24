namespace XDrishti.Application.Reporting;

/// <summary>
/// The raw text of the one query model of the report endpoints (P8-00 D4): what the query string carries, before the whitelist. Lists are comma separated. There is deliberately no field for the basis confirmation: whether a report may be net is decided from the
/// owner's stored confirmation only, never from what a caller says.
/// </summary>
/// <param name="Runs">Run keys (64 lower-case hex each); empty means the default set: the newest run of each strategy and universe.</param>
/// <param name="Basis"><c>gross</c> (default) or <c>net</c>.</param>
/// <param name="Lens">Dimensions to group by, in order: basket, instrument, strategy, timeframe, side, style, exit, month, weekday (regime and account are named and refused with the reason).</param>
/// <param name="Strategy">Strategy ids to keep.</param>
/// <param name="Timeframe">Timeframe codes to keep (<c>15m</c>).</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="Style">Run style labels to keep (<c>intraday</c>, <c>swing</c>).</param>
/// <param name="Exit">Run exit labels to keep, separated by semicolons (a label such as <c>FIXED_RR{rr=2,x=3}</c> may hold a comma).</param>
/// <param name="Basket">Basket ids to keep.</param>
/// <param name="Symbol">Symbols to keep.</param>
/// <param name="From">First entry date to keep, <c>yyyy-MM-dd</c>.</param>
/// <param name="To">Last entry date to keep, <c>yyyy-MM-dd</c>.</param>
/// <param name="Metric">The figure of a pivot (snake case, <c>expectancy_r</c> by default).</param>
/// <param name="Limit">Page size of the trade list, 1 to 1,000 (100 by default).</param>
/// <param name="Cursor">The cursor the previous page of the trade list returned.</param>
public sealed record ReportQueryInput(
    string? Runs = null,
    string? Basis = null,
    string? Lens = null,
    string? Strategy = null,
    string? Timeframe = null,
    string? Side = null,
    string? Style = null,
    string? Exit = null,
    string? Basket = null,
    string? Symbol = null,
    string? From = null,
    string? To = null,
    string? Metric = null,
    string? Limit = null,
    string? Cursor = null);
