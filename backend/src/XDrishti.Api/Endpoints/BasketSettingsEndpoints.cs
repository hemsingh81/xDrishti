using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Selection;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner's basket settings (spec BK-08): where the market gate reads breadth from, the cap on open risk per correlation cluster and the cut of the clusters, plus the read-only daily context of a basket. Reading is open to the session; every change needs the session,
/// the CSRF token and a fresh password for THIS change, tightening and loosening alike, and the actor comes from the session. Nothing here places an order.
/// </summary>
internal sealed class BasketSettingsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var selection = api.MapGroup("/selection").WithTags("Selection");

        selection.MapGet("/basket-settings", GetAsync)
            .WithName("GetBasketSettings")
            .WithSummary("The basket settings in force (the gate's breadth source, tracked by default or basket:<id>; the cap on open risk per correlation cluster, percent of capital, off by default; the cluster cut, 0.70) with the version, the newest loosening change and the latest 50 changes (key, old and new value, direction tighten, loosen or neutral, actor, time, hash). Read only");

        selection.MapPut("/basket-settings", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeBasketSettings")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner changes the basket settings (breadthSource tracked or basket:<id>, maxClusterRiskPct above 0 up to 100 or clearCap true to turn the cap off, clusterRho 0.1 to 1.0); a value left out stays. The password is needed on EVERY change (401 auth.invalid_credentials, 423 auth.locked); each key that changes is appended as a row with its direction. 400 selection.invalid, 403 selection.owner_only, 404 selection.basket_not_found, 409 selection.stale");

        api.MapGroup("/market").WithTags("Market")
            .MapGet("/basket-context", ContextAsync)
            .WithName("GetBasketContext")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("The stored daily context of one basket (basket, optional from and to as yyyy-MM-dd; the newest 400 rows, newest first): members, voters, the 20, 50 and 200 day breadth, advance and new-high shares, dispersion, average correlation, state and membership basis (point_in_time or single_snapshot). Descriptive only; read only");
    }

    private static async Task<Ok<BasketSettingsView>> GetAsync(IQueryHandler<GetBasketSettingsQuery, BasketSettingsView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetBasketSettingsQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<BasketSettingsView>, ProblemHttpResult>> ChangeAsync(BasketSettingsChangeBody request, HttpContext http, ICommandHandler<ChangeBasketSettingsCommand, BasketSettingsView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var session = http.GetVerifiedSession();
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure || session is null)
        {
            return ProblemResults.From(owner.IsFailure ? owner.Error : AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new ChangeBasketSettingsCommand(owner.Value, session.SessionId, http.GetClientInfo(), request.BreadthSource, request.MaxClusterRiskPct, request.ClearCap ?? false, request.ClusterRho, request.Password), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<BasketContextView>, ProblemHttpResult>> ContextAsync(Guid? basket, DateOnly? from, DateOnly? to, IQueryHandler<GetBasketContextQuery, Result<BasketContextView>> handler, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetBasketContextQuery(basket, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }
}
