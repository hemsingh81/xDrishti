using Microsoft.AspNetCore.Mvc;
using XDrishti.Application.Reporting;

namespace XDrishti.Api.Endpoints;

/// <summary>The query string of the reports that also take a lens (groups, equity, distribution, what-works): the run set, the basis, the lens and the filters. No confirmation of the charge sheet can be passed. Matching of strategy, symbol, exit and basket is exact and case-sensitive.</summary>
internal sealed record ReportLensParams(
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
    [FromQuery(Name = "to")] string? To = null)
{
    /// <summary>The raw input for the Application.</summary>
    public ReportQueryInput ToInput() => new(Runs, Basis, Lens, Strategy, Timeframe, Side, Style, Exit, Basket, Symbol, From, To);
}
