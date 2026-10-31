using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner's switch writes and the kill-switch drill (specs P12-07 criterion 13, P12-05 criterion 10, P12-10 criteria 7 and 9; ADR 0174). Every write needs the session, the CSRF token and a fresh password for THIS act; the actor comes from the
/// session. Nothing here can send an order: <c>auto_live_enabled</c> cannot be turned on and the drill runs only in the execution service's sandbox composition with a transport that refuses everything.
/// </summary>
internal sealed class ExecutionSwitchEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/execution").WithTags("Execution");

        group.MapGet("/settings", GetSettingsAsync)
            .WithName("GetExecutionSettings")
            .WithSummary("The two global execution switches: the row in force (null: never written, both OFF), the raise waiting for its effectiveFrom, the latest 20 rows, autoLiveLocked (always true: no route turns auto_live_enabled on) and serverNow (the database clock). Read only");

        group.MapPut("/settings", PutSettingsAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeExecutionSettings")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner switches execution_enabled, with a reason of 1 to 300 characters and the password on EVERY change (400 execution.password_required, 401 auth.invalid_credentials, 423 auth.locked). Switching it ON while OFF is in force needs the gate Live:Writes:Enabled (checked BEFORE the password; 403 live.capability_off) and takes effect at 09:00 India time of the next trading day after one full closed session; OFF is in force at once and supersedes a waiting raise. autoLiveEnabled true is always 409 execution.auto_live_locked. 400 execution.values_invalid, 403 execution.owner_only, 409 execution.stale when the database refused the row. The values object is not changed here");

        group.MapGet("/drills", GetDrillsAsync)
            .WithName("GetExecutionDrills")
            .WithSummary("The kill-switch drills newest first (at most 50: passed, latency in ms, what was proved), the latest 20 drill requests with the execution service's answer (execution.drill_passed, execution.drill_failed, execution.drill_refused:<why>, execution.drill_unavailable outside the sandbox; null while unanswered), maxAgeDays (30: the ladder gate needs a drill no older) and serverNow. Optional accountId. Read only");

        group.MapPost("/drills", PostDrillAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("RequestExecutionDrill")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("Run drill: leaves a drill command for the execution service (the answer is the command id; read the drills to see the result). Needs the session, the CSRF token and a fresh password (401 auth.invalid_credentials, 423 auth.locked). The service runs it only in the sandbox: it refuses when the account holds a position or a working order or any halt is active, raises a cancel halt of its own, proves that an entry is stopped at the send path before a transport that refuses everything, clears its halt and stores the row. No order is sent in any case. 409 execution.drill_refused while a halt is in force, 404 execution.account_unknown, 400 execution.values_invalid");
    }

    private static async Task<Ok<ExecutionSettingsView>> GetSettingsAsync(IQueryHandler<GetExecutionSettingsQuery, ExecutionSettingsView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetExecutionSettingsQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<ExecutionSettingsView>, ProblemHttpResult>> PutSettingsAsync(
        ExecutionSettingsChangeBody request, HttpContext http, ICommandHandler<ChangeExecutionSettingsCommand, ExecutionSettingsView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var session = http.GetVerifiedSession();
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure || session is null)
        {
            return ProblemResults.From(owner.IsFailure ? owner.Error : AuthErrors.Unauthenticated, http);
        }

        // The handler decides whether this is a raise, asks the live-writes gate first for one, and then checks the fresh password for every change.
        var result = await handler.HandleAsync(
            new ChangeExecutionSettingsCommand(owner.Value, session.SessionId, http.GetClientInfo(), request.ExecutionEnabled, request.AutoLiveEnabled, request.Reason, request.Password), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Ok<ExecutionDrillsView>> GetDrillsAsync(IQueryHandler<GetExecutionDrillsQuery, ExecutionDrillsView> handler, HttpContext http, CancellationToken cancellationToken, Guid? accountId = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetExecutionDrillsQuery(accountId), cancellationToken));
    }

    private static async Task<Results<Ok<ExecutionDrillStarted>, ProblemHttpResult>> PostDrillAsync(
        DrillRequestBody request, HttpContext http, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<RequestExecutionDrillCommand, ExecutionDrillStarted> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new RequestExecutionDrillCommand(owner.Value, request.AccountId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }
}
