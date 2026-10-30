using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The per-account limits sheet of system-sent entries (spec P12-09, ADR 0167): read it, and append a new snapshot. A raise needs a fresh password and waits for one full closed session; a pure lowering needs only the session and is in force at
/// once. The actor comes from the session. Nothing here can send an order.
/// </summary>
internal sealed class ExecutionLimitsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/execution").WithTags("Execution");

        group.MapGet("/limits", GetAsync)
            .WithName("GetExecutionLimits")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The limits sheet of one account (accountId): the row in force (null: no sheet, nothing is allowed), the raise waiting for its effectiveFrom, complete and the missing fields (an incomplete sheet allows nothing), the latest 20 rows, the compiled ceilings and serverNow (the database clock). Read only");

        group.MapPut("/limits/{accountId:guid}", PutAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here for a raise, so the sign-in limit applies
            .WithName("ChangeExecutionLimits")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner's new limits sheet for one account, stored as a complete snapshot (an unset value allows nothing). A change that loosens anything against the row in force (a cap up, a wider window, a symbol or product added, exitAllOnLossHalt switched OFF; a mixed change counts as a raise) needs the password (400 execution.password_required, 401 auth.invalid_credentials, 423 auth.locked) and takes effect at 09:00 India time of the next trading day after one full closed session; a pure lowering needs only the session and the CSRF token and takes effect at once. 400 execution.values_invalid names the field (ceilings: 20 orders a day, 10 open positions, 50 symbols, window 09:16 to 15:00, products EQ_MIS only); 409 execution.disabled when the account's Trading role is off and the change is a raise; 409 execution.stale when the database refused the row. The answer is the sheet view with the row in force and the pending row");
    }

    private static async Task<Results<Ok<LimitsView>, ProblemHttpResult>> GetAsync(
        IQueryHandler<GetExecutionLimitsQuery, Result<LimitsView>> handler, HttpContext http, CancellationToken cancellationToken, Guid? accountId = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetExecutionLimitsQuery(accountId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<LimitsView>, ProblemHttpResult>> PutAsync(
        Guid accountId, LimitsChangeBody request, HttpContext http, ICommandHandler<ChangeExecutionLimitsCommand, LimitsView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var session = http.GetVerifiedSession();
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure || session is null)
        {
            return ProblemResults.From(owner.IsFailure ? owner.Error : AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(
            new ChangeExecutionLimitsCommand(
                owner.Value, session.SessionId, http.GetClientInfo(), accountId, request.MaxOrdersPerDay, request.MaxNotionalPerOrder, request.MaxNotionalPerDay, request.MaxLossPerDay, request.MaxOpenPositions, request.AllowList,
                request.WindowStart, request.WindowEnd, request.Products, request.ExitAllOnLossHalt, request.Reason, request.Password),
            cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }
}
