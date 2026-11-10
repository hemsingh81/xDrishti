using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Sizing;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The down-only size scaling (spec BK-10): the owner's settings and basket risk budgets, and the read-only sizing study. Reading is open to the session; every change needs the session, the CSRF token and a fresh password for THIS change, loosening and tightening alike,
/// and the actor comes from the session. Sizing only ever scales a suggestion down; there is no setting that scales up and nothing here places an order.
/// </summary>
internal sealed class SizingEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var selection = api.MapGroup("/selection").WithTags("Selection");

        selection.MapGet("/sizing-settings", GetSettingsAsync)
            .WithName("GetSizingSettings")
            .WithSummary("The sizing settings in force (volatility targeting and the capacity cap, both OFF by default, the floor 0.5, the capacity share 0.01) with the version, the newest loosening change and the latest 50 changes (key, old and new value, direction tighten, loosen or neutral, actor, time, hash). Sizing only scales a suggestion down. Read only");

        selection.MapPut("/sizing-settings", ChangeSettingsAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeSizingSettings")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner changes the sizing settings (volTargetEnabled, floor 0.25 to 1.0, capacityEnabled, capacityPct above 0 and at most 0.10); a value left out stays. The password is needed on EVERY change (401 auth.invalid_credentials, 423 auth.locked); each key that changes is appended as a row with its direction. 400 selection.invalid, 403 selection.owner_only, 409 selection.stale");

        selection.MapGet("/basket-risk-budgets", GetBudgetsAsync)
            .WithName("GetBasketRiskBudgets")
            .WithSummary("The basket risk budgets in force (a basket's share, 1 to 100 percent, of the day's total open risk limit; a basket with no row has no budget) and the latest 50 changes. Read only");

        selection.MapPut("/basket-risk-budgets/{basketId:guid}", ChangeBudgetAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ChangeBasketRiskBudget")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner sets one basket's risk budget (budgetPct 1 to 100, or null to remove it) with the password on EVERY change. Raising or removing a budget is recorded as loosen. 400 selection.invalid, 403 selection.owner_only, 404 selection.basket_not_found, 409 selection.stale");

        api.MapGroup("/reports").WithTags("Reports")
            .MapGet("/sizing-study", StudyAsync)
            .WithName("GetSizingStudy")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("What the stored closed trades of one run (run, the 64-character key) would have done with the down-only volatility scale, on the same trades: cumulative net R, maximum drawdown and total with and without it, the two halves (a half below 30 trades is insufficient), the trades with no scale. Optional from and to (entry dates) and floor (0.25, 0.5 or 0.75 as a labelled sensitivity; left out it is the stored floor). Descriptive, before costs, recommends nothing, writes nothing, never scales above 1. 422 reports.invalid or reports.too_large");
    }

    private static async Task<Ok<SizingSettingsView>> GetSettingsAsync(IQueryHandler<GetSizingSettingsQuery, SizingSettingsView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetSizingSettingsQuery(), cancellationToken));
    }

    private static async Task<Ok<BasketRiskBudgetsView>> GetBudgetsAsync(IQueryHandler<GetBasketRiskBudgetsQuery, BasketRiskBudgetsView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetBasketRiskBudgetsQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<SizingSettingsView>, ProblemHttpResult>> ChangeSettingsAsync(SizingSettingsChangeBody request, HttpContext http, ICommandHandler<ChangeSizingSettingsCommand, SizingSettingsView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var session = http.GetVerifiedSession();
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure || session is null)
        {
            return ProblemResults.From(owner.IsFailure ? owner.Error : AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new ChangeSizingSettingsCommand(owner.Value, session.SessionId, http.GetClientInfo(), request.VolTargetEnabled, request.Floor, request.CapacityEnabled, request.CapacityPct, request.Password), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<BasketRiskBudgetsView>, ProblemHttpResult>> ChangeBudgetAsync(Guid basketId, BasketRiskBudgetChangeBody request, HttpContext http, ICommandHandler<ChangeBasketRiskBudgetCommand, BasketRiskBudgetsView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var session = http.GetVerifiedSession();
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure || session is null)
        {
            return ProblemResults.From(owner.IsFailure ? owner.Error : AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new ChangeBasketRiskBudgetCommand(owner.Value, session.SessionId, http.GetClientInfo(), basketId, request.BudgetPct, request.Password), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<SizingStudyView>, ProblemHttpResult>> StudyAsync(string? run, DateOnly? from, DateOnly? to, double? floor, IQueryHandler<GetSizingStudyQuery, Result<SizingStudyView>> handler, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetSizingStudyQuery(run, from, to, floor), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.Kind == ErrorKind.Validation && result.Error.Code.StartsWith("reports.", StringComparison.Ordinal)
                ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Message, result.Error.Code))
                : ProblemResults.From(result.Error, http);
    }
}
