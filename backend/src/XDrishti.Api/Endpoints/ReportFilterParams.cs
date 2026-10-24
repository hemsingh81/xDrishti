using Microsoft.AspNetCore.Mvc;
using XDrishti.Application.Reporting;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The query string of the reports that take the run set, the basis and the filters only (summary, insights), with its wire names, bound by <c>[AsParameters]</c>. It is only the raw text of <see cref="ReportQueryInput"/>, which the Application checks against its whitelists.
/// There is deliberately no parameter that could say the charge sheet was confirmed: whether a report may be net is decided from the owner's stored confirmation only. Matching of strategy, symbol, exit and basket is exact and case-sensitive.
/// </summary>
internal sealed record ReportFilterParams(
    [FromQuery(Name = "runs")] string? Runs = null,
    [FromQuery(Name = "basis")] string? Basis = null,
    [FromQuery(Name = "strategy")] string? Strategy = null,
    [FromQuery(Name = "timeframe")] string? Timeframe = null,
    [FromQuery(Name = "side")] string? Side = null,
    [FromQuery(Name = "style")] string? Style = null,
    [FromQuery(Name = "exit")] string? Exit = null,
    [FromQuery(Name = "basket")] string? Basket = null,
    [FromQuery(Name = "symbol")] string? Symbol = null,
    [FromQuery(Name = "from")] string? From = null,
    [FromQuery(Name = "to")] string? To = null)
{
    /// <summary>The raw input for the Application.</summary>
    public ReportQueryInput ToInput() => new(Runs, Basis, null, Strategy, Timeframe, Side, Style, Exit, Basket, Symbol, From, To);
}
