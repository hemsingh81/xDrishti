using Microsoft.AspNetCore.Mvc;

namespace XDrishti.Api.Endpoints;

/// <summary>The query string of the basket profile (BK-07): the raw text, checked by the Application.</summary>
internal sealed record BasketProfileParams(
    [FromRoute(Name = "id")] string? Id = null,
    [FromQuery(Name = "from")] string? From = null,
    [FromQuery(Name = "to")] string? To = null,
    [FromQuery(Name = "index")] string? Index = null,
    [FromQuery(Name = "membersAsOf")] string? MembersAsOf = null);

/// <summary>The query string of the basket ranking (BK-07): the raw text, checked by the Application.</summary>
internal sealed record BasketRankingParams(
    [FromQuery(Name = "basket")] string? Basket = null,
    [FromQuery(Name = "from")] string? From = null,
    [FromQuery(Name = "to")] string? To = null,
    [FromQuery(Name = "index")] string? Index = null);

/// <summary>The body of the choice of the basket benchmark (BK-06c): the id of a listed index of the instrument master.</summary>
internal sealed record SetBasketBenchmarkInput(Guid? InstrumentId);
