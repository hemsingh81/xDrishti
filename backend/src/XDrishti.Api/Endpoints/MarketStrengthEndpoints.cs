using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Strength;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The stored relative-strength rows of BK-09 (momentum rank inside a pre-registered basket and 20-session strength against the Nifty 50), for the Lab and the reports. A read: nothing here writes.</summary>
internal sealed class MarketStrengthEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/market").WithTags("Market");

        group.MapGet("/relative-strength", GetAsync)
            .WithName("GetRelativeStrength")
            .WithSummary("The stored relative-strength rows, newest session first, at most 500: per share and pre-registered basket the volatility-adjusted momentum score and its percent rank (0 to 1) for the lookbacks 126 and 252 skipping the latest 21 sessions, with the number ranked, the state (value, insufficient, not_evaluable, unavailable, short_history, corporate_action_unresolved) and the membership basis (dated, or single_snapshot with survivorship). A rank is a number only for a value. Takes basket (an id or one of the names Nifty 50, Nifty 100, F&O stocks, Most liquid) and asOf (yyyy-MM-dd, the last closed session the row is as of). Read only; the rows are written once by the daily step, which is off until Market:RelativeStrength:Enabled. 400 market.relative_strength.invalid.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<RelativeStrengthView>, ProblemHttpResult>> GetAsync(
        string? basket,
        string? asOf,
        HttpContext http,
        IQueryHandler<GetRelativeStrengthQuery, Result<RelativeStrengthView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetRelativeStrengthQuery(basket, asOf), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
