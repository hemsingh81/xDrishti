using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The body of <c>PUT /api/auto/scope</c>: ids of baskets and of individual instruments; at least one in all.</summary>
internal sealed record SetAutoScopeRequest(IReadOnlyList<Guid>? BasketIds, IReadOnlyList<Guid>? InstrumentIds);

/// <summary>
/// The auto scope and the reasoned next-day suggestions (spec AU-16, ADR 0251): read and set the scope (set as the owner, audited), and read the suggestions of a plan with reasons and evidence.
/// A suggestion is not an order and creates no approval or arming; nothing here places, changes or cancels an order.
/// </summary>
internal sealed class AutoEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/auto").WithTags("Auto");

        group.MapGet("/scope", GetScopeAsync)
            .WithName("GetAutoScope")
            .WithSummary("The baskets and stocks the system may suggest (suggest.scope); with none set, every eligible instrument is considered");

        group.MapPut("/scope", SetScopeAsync)
            .WithName("SetAutoScope")
            .WithSummary("Set the scope as the owner (session and CSRF token); appends a version and an audit row. 400 auto.scope_empty, auto.scope_too_large, auto.scope_unknown (an id that does not exist), 403 auto.owner_only")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapGet("/suggestions", GetSuggestionsAsync)
            .WithName("GetAutoSuggestions")
            .WithSummary("The next-day suggestions of the newest plan of forDate (or the newest plan): what, why and on what evidence; missing figures read unavailable, tickets without required evidence are listed as not suggested; reserve and rejected stay visible. Suggestion only, no order is placed. 404 auto.no_plan");
    }

    private static async Task<Ok<AutoScopeView>> GetScopeAsync(IQueryHandler<GetAutoScopeQuery, AutoScopeView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetAutoScopeQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<AutoScopeView>, ProblemHttpResult>> SetScopeAsync(
        SetAutoScopeRequest request,
        HttpContext http,
        ICommandHandler<SetAutoScopeCommand, AutoScopeView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new SetAutoScopeCommand(owner.Value, request.BasketIds, request.InstrumentIds), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<AutoSuggestionsView>, ProblemHttpResult>> GetSuggestionsAsync(
        DateOnly? forDate,
        HttpContext http,
        IQueryHandler<GetAutoSuggestionsQuery, Result<AutoSuggestionsView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAutoSuggestionsQuery(forDate), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
