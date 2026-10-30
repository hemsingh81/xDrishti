using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The status of the execution service (spec P12-01b). One read-only route: it shows the service's heartbeat, its environment, each account's gate and the count of open intents. It has no write route and instructs nothing; the API reaches the service only
/// through rows in <c>execution.commands</c>, and nothing in this slice writes one.
/// </summary>
internal sealed class ExecutionEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/execution").WithTags("Execution");

        group.MapGet("/status", GetAsync)
            .WithName("GetExecutionStatus")
            .WithSummary("The execution service: down / recovering / ready / halted from its heartbeat (down when older than 30 s), its process environment (none, sandbox, live), each account's mode and gate (closed until the mode ladder exists), halts and the open intents. Read only; an empty database answers environment none and closed gates");

        group.MapGet("/alarms", GetAlarmsAsync)
            .WithName("GetExecutionAlarms")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("The alarms of the execution service's protection logic, newest first (at most 200): protection pending or breached, flatten started or incomplete, oversold and the rest. open=true keeps only those not cleared. Read only; an alarm is cleared by the engine or the owner, never deleted");

        group.MapGet("/halts", GetHaltsAsync)
            .WithName("GetExecutionHalts")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("The halts of the execution service (the kill switch), newest first (at most 200): scope, account, level (entries, cancel, exit_all, freeze), the trigger code, who started it and, once it ended, how (owner release or automatic clear). active=true keeps only those in force. Read only");

        group.MapPost("/halts", StartHaltAsync)
            .WithName("StartExecutionHalt")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("The owner's button: leaves a halt command for the execution service, which records the halt (the answer is the command id; read the halts to see it). Needs a session and the CSRF token and NO password: starting a halt is the safe direction. exit_all additionally needs the step-up window (403 auth.step_up_required) because it realises losses. 400 execution.halt_invalid");

        group.MapPost("/halts/{id:long}/release", ReleaseHaltAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ReleaseExecutionHalt")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Releases a halt as the owner: only the owner can, with the gate Live:Writes:Enabled on (checked BEFORE the password; 403 live.capability_off), a fresh password for this act (401 auth.invalid_credentials; an earlier step-up window does not count), the actor from the session and a reason. Refused with 409 execution.release_refused and every reason when the halt already ended, the reason is missing or too short (a drawdown halt needs 20 characters), an exit_all halt still has an open position, a protection halt still has an open protection alarm, or the halt is a freeze (released only when it came from a reconciliation break and every break of its account has a resolution). 404 execution.halt_unknown");

        group.MapGet("/reconciliation", GetReconciliationAsync)
            .WithName("GetExecutionReconciliation")
            .WithSummary("The reconciliation runs and breaks of the execution service (at most 200 of each, newest first): per run the kind (continuous, eod, final, recovery), whether the broker could be read and how many breaks; per break the kind (position_mismatch, trade_missing, trade_extra, order_mismatch, fill_mismatch, unknown_xd_order, foreign_conflict, unavailable), the account, the figures' summary and the owner's resolution if any. Optional date (the session) and accountId. Read only; a break is never corrected automatically");

        group.MapPost("/breaks/{id:long}/resolve", ResolveBreakAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ResolveExecutionBreak")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Resolves a reconciliation break as the owner: only the owner can, with the gate Live:Writes:Enabled on (checked BEFORE the password; 403 live.capability_off), a fresh password for this act (401 auth.invalid_credentials; an earlier step-up window does not count) and the actor from the session. resolution is accept_broker (the engine then appends an adoption event; nothing is rewritten) or dismiss_foreign (a foreign conflict only); note is at most 300 characters. 400 execution.resolution_invalid, 404 execution.break_unknown, 409 execution.break_resolved. The freeze it caused can be released once every break of the account is resolved");

        group.MapGet("/modes", GetModesAsync)
            .WithName("GetExecutionModes")
            .WithSummary("The execution mode of each account with a Trading role (paper, manual_live, assisted): configured and effective, a change waiting for the next open, the open promotion request, the evidence for the next step (every check with pass, fail or unavailable; unavailable never passes) and the latest 20 trail rows. Read only; no token, secret, client id or balance");

        group.MapPost("/modes/{accountId:guid}/promotion-requests", RequestPromotionAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("RequestExecutionPromotion")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("The owner asks for the next mode (one step up): only the owner, with Live:Writes:Enabled on (checked BEFORE the password; 403 live.capability_off), a fresh password and a reason. Stores the evidence and its hash and a window (confirmable from the close of the second trading session, until the close of the fifth) and grants nothing. 409 execution.mode_not_adjacent, execution.mode_gate_failed with every reason (unavailable never passes). Only assisted exists; semi_auto and auto_live are 400 execution.values_invalid");

        group.MapPost("/modes/{accountId:guid}/promotion-requests/{id:guid}/confirm", ConfirmPromotionAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ConfirmExecutionPromotion")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("The owner confirms the request inside its window, with the live-writes gate before a fresh password. The gate is evaluated again. 409 execution.cooling_off, execution.request_expired, execution.mode_gate_failed, execution.stale. Writes one trail row that takes effect at 09:00 India time of the next trading day");

        group.MapPost("/modes/{accountId:guid}/demotions", DemoteAsync)
            .WithName("DemoteExecutionMode")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("The owner lowers the mode at once: a session and the CSRF token and NO password and no live-writes gate, because it lowers risk. 409 execution.mode_not_lower unless the target is below the effective mode");
    }

    private static async Task<Ok<ExecutionModesView>> GetModesAsync(IQueryHandler<GetExecutionModesQuery, ExecutionModesView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetExecutionModesQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<ExecutionPromotionRequestView>, ProblemHttpResult>> RequestPromotionAsync(
        Guid accountId, PromotionRequestBody request, HttpContext http, ILiveWritesGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<RequestPromotionCommand, ExecutionPromotionRequestView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!gate.Enabled)
        {
            return ProblemResults.From(Error.Forbidden("live.capability_off", "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new RequestPromotionCommand(owner.Value, accountId, request.ToMode, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<ExecutionTrailView>, ProblemHttpResult>> ConfirmPromotionAsync(
        Guid accountId, Guid id, ConfirmPromotionBody request, HttpContext http, ILiveWritesGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<ConfirmPromotionCommand, ExecutionTrailView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!gate.Enabled)
        {
            return ProblemResults.From(Error.Forbidden("live.capability_off", "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ConfirmPromotionCommand(owner.Value, accountId, id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<ExecutionTrailView>, ProblemHttpResult>> DemoteAsync(
        Guid accountId, DemotionBody request, HttpContext http, ICommandHandler<DemoteModeCommand, ExecutionTrailView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new DemoteModeCommand(owner.Value, accountId, request.ToMode, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Ok<ExecutionReconciliationView>> GetReconciliationAsync(
        IQueryHandler<GetExecutionReconciliationQuery, ExecutionReconciliationView> handler, HttpContext http, CancellationToken cancellationToken, DateOnly? date = null, Guid? accountId = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetExecutionReconciliationQuery(date, accountId), cancellationToken));
    }

    private static async Task<Results<Ok<ExecutionBreakView>, ProblemHttpResult>> ResolveBreakAsync(
        long id, ResolveBreakRequest request, HttpContext http, ILiveWritesGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<ResolveExecutionBreakCommand, ExecutionBreakView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // The order of every owner write: the gate first, then the fresh password for THIS act, then the actor from the session.
        if (!gate.Enabled)
        {
            return ProblemResults.From(Error.Forbidden("live.capability_off", "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ResolveExecutionBreakCommand(owner.Value, id, request.Resolution, request.Note), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<IReadOnlyList<ExecutionHaltView>>, ProblemHttpResult>> GetHaltsAsync(
        IQueryHandler<GetExecutionHaltsQuery, Result<IReadOnlyList<ExecutionHaltView>>> handler, HttpContext http, CancellationToken cancellationToken, bool active = false, Guid? accountId = null, int take = 200)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetExecutionHaltsQuery(active, accountId, take), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<ExecutionHaltStarted>, ProblemHttpResult>> StartHaltAsync(
        StartHaltRequest request, HttpContext http, ICommandHandler<StartExecutionHaltCommand, ExecutionHaltStarted> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var stepUp = http.GetVerifiedSession()?.Info.ElevatedUntil is not null;
        var result = await handler.HandleAsync(new StartExecutionHaltCommand(owner.Value, request.Scope ?? string.Empty, request.AccountId, request.Level ?? string.Empty, request.Reason, request.BrokerKillSwitch ?? false, stepUp), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<ExecutionHaltView>, ProblemHttpResult>> ReleaseHaltAsync(
        long id, ReleaseHaltRequest request, HttpContext http, ILiveWritesGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<ReleaseExecutionHaltCommand, ExecutionHaltView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // The order of every owner write: the gate first (a switched-off act neither counts toward the sign-in lockout nor says anything about the password), then the fresh password for THIS act, then the actor from the session.
        if (!gate.Enabled)
        {
            return ProblemResults.From(Error.Forbidden("live.capability_off", "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ReleaseExecutionHaltCommand(owner.Value, id, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<IReadOnlyList<ExecutionAlarmView>>, ProblemHttpResult>> GetAlarmsAsync(
        IQueryHandler<GetExecutionAlarmsQuery, Result<IReadOnlyList<ExecutionAlarmView>>> handler, HttpContext http, CancellationToken cancellationToken, bool open = false, Guid? accountId = null, int take = 200)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetExecutionAlarmsQuery(open, accountId, take), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Ok<ExecutionStatusView>> GetAsync(IQueryHandler<GetExecutionStatusQuery, ExecutionStatusView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetExecutionStatusQuery(), cancellationToken));
    }
}
