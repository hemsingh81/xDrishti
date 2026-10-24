using Microsoft.AspNetCore.Mvc;
using XDrishti.Application.Reporting;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The query string of the report endpoints with its wire names (lower case), bound by <c>[AsParameters]</c>; it is only the raw text of <see cref="ReportQueryInput"/>, which the Application checks against its whitelists. There is deliberately no parameter that
/// could say the charge sheet was confirmed: whether a report may be net is decided from the owner's stored confirmation only.
/// </summary>
internal sealed record ReportQueryParams(
    [FromQuery(Name = "runs")] string? Runs = null,
    [FromQuery(Name = "basis")] string? Basis = null,
    [FromQuery(Name = "lens")] string? Lens = null,
    [FromQuery(Name = "strategy")] string? Strategy = null,
    [FromQuery(Name = "timeframe")] string? Timeframe = null,
    [FromQuery(Name = "side")] string? Side = null,
    [FromQuery(Name = "style")] string? Style = null,
    [FromQuery(Name = "exit")] string? Exit = null,
    [FromQuery(Name = "basket")] string? Basket = null,
    [FromQuery(Name = "symbol")] string? Symbol = null,
    [FromQuery(Name = "from")] string? From = null,
    [FromQuery(Name = "to")] string? To = null,
    [FromQuery(Name = "metric")] string? Metric = null,
    [FromQuery(Name = "limit")] string? Limit = null,
    [FromQuery(Name = "cursor")] string? Cursor = null)
{
    /// <summary>The raw input for the Application.</summary>
    public ReportQueryInput ToInput() => new(Runs, Basis, Lens, Strategy, Timeframe, Side, Style, Exit, Basket, Symbol, From, To, Metric, Limit, Cursor);
}
