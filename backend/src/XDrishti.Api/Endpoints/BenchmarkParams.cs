using Microsoft.AspNetCore.Mvc;

namespace XDrishti.Api.Endpoints;

/// <summary>The query string of the basket benchmarks (P8-07): the raw text, checked by the Application.</summary>
internal sealed record BenchmarkParams(
    [FromQuery(Name = "basket")] string? Basket = null,
    [FromQuery(Name = "from")] string? From = null,
    [FromQuery(Name = "to")] string? To = null,
    [FromQuery(Name = "membersAsOf")] string? MembersAsOf = null);
