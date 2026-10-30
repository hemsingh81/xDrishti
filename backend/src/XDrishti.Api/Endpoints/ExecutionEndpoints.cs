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

        group.MapGet("/overview", GetOverviewAsync)
            .WithName("GetExecutionOverview")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Today at a glance, per account (optional date, default the current session in India): effective mode and since when, the meters (orders, notional, worst-case loss, open positions) against the limits sheet in force with the pending raise beside it, open tickets, halts, alarms and positions without confirmed protection, the canary switch and the evidence line. A figure is value, insufficient, unavailable, not_evaluable or empty and an unreadable input is unavailable with its reason, never 0. Read only; no token, client id, secret or balance");

        group.MapGet("/blotter", GetBlotterAsync)
            .WithName("GetExecutionBlotter")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("The orders of a day, newest first, 50 a page (date, accountId, stateGroup pending|working|live|done|stopped, page from 1): state in P12-01's words (an unknown word is passed through with stateKnown false), who caused it (tap, auto, system, owner), broker ids as received, per-fill slippage in ticks and percent against the ticket price (positive is worse), rejection codes and the broker code. 422 execution.invalid on an unknown filter. Read only");

        group.MapGet("/positions", GetPositionsAsync)
            .WithName("GetExecutionPositions")
            .WithSummary("The open positions (optional accountId): net quantity, average, last price and unrealised result (unavailable when the price is older than the feed limit), risk to the stop, protection confirmed|missing|unknown (confirmed only from a recent clean read of the broker's order book), source ours|foreign and reconciliation matched|break|not_run|unavailable. Read only");

        group.MapGet("/activity", GetActivityAsync)
            .WithName("GetExecutionActivity")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("The action log with the alarms, halts, drills, mode and settings changes, newest first, 50 a page (date, kind, by auto|tap|system|owner, page from 1). 422 execution.invalid on an unknown filter. Read only: the log is insert-only");

        group.MapGet("/notices", GetNoticesAsync)
            .WithName("GetExecutionNotices")
            .WithSummary("The execution items for the bell, derived on every read from insert-only rows: exec_confirm_waiting, exec_auto_action, exec_order_rejected, exec_protection_missing, exec_halted, exec_kill_switch, exec_demoted, exec_recon_break, exec_not_ready, exec_ip_invalid, exec_cap_near, exec_day_summary. Each id includes its cause; a resolved item stays 24 hours and is not counted. Read only");

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

        group.MapGet("/readiness", GetReadinessAsync)
            .WithName("GetExecutionReadiness")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Whether execution can act today, per account (optional date, default the current session in India): state ready, not_ready or unavailable (also when nothing was ever recorded; only ready lets anything act), the six checks with pass, fail or unavailable and their ages, when the token ends, since when the account has been ready, the first day the IP can be changed at Dhan, the owner's newest IP attestation, P11-05's newest SEBI re-check, the last 20 states recorded at 09:10:30 India time and the highest sends per second. Read only; no token, client id, key or secret");

        group.MapPost("/readiness/checks", RequestReadinessCheckAsync)
            .WithName("RequestExecutionReadinessCheck")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Check now: leaves a readiness_now command row; the execution service probes within a second and the new record shows in GET /readiness. A session and the CSRF token, no password. It reads two things at the broker (profile and configured IP) and sends nothing");

        group.MapPost("/ip-attestations", RecordIpAttestationAsync)
            .WithName("RecordExecutionIpAttestation")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The owner records that Dhan's web tool (IP Setup, Get Static IP) showed orders allowed (or not) for an IP: accountId, ip (one IPv4 or IPv6 address), ordersAllowed and a plain-text note of at most 200 characters. A session and the CSRF token, the actor from the session, the time from the database. It is a claim and never contacts the broker; ordersAllowed false is recorded and keeps the attestation check failing. 400 execution.values_invalid, 404 execution.not_found");

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

    private static async Task<Results<Ok<ExecutionReadinessView>, ProblemHttpResult>> GetReadinessAsync(
        IQueryHandler<GetExecutionReadinessQuery, Result<ExecutionReadinessView>> handler, HttpContext http, CancellationToken cancellationToken, DateOnly? date = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetExecutionReadinessQuery(date), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<ExecutionReadinessRequested>, ProblemHttpResult>> RequestReadinessCheckAsync(
        ICommandHandler<RequestReadinessCheckCommand, ExecutionReadinessRequested> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new RequestReadinessCheckCommand(owner.Value), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<ExecutionIpAttestationView>, ProblemHttpResult>> RecordIpAttestationAsync(
        IpAttestationRequest request, ICommandHandler<RecordIpAttestationCommand, ExecutionIpAttestationView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new RecordIpAttestationCommand(owner.Value, request.AccountId, request.Ip, request.OrdersAllowed, request.Note), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
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

    private static async Task<Results<Ok<ExecutionOverview>, ProblemHttpResult>> GetOverviewAsync(
        IQueryHandler<GetExecutionOverviewQuery, Result<ExecutionOverview>> handler, HttpContext http, CancellationToken cancellationToken, DateOnly? date = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetExecutionOverviewQuery(date), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error, http);
    }

    private static async Task<Results<Ok<IReadOnlyList<BlotterRow>>, ProblemHttpResult>> GetBlotterAsync(
        IQueryHandler<GetExecutionBlotterQuery, Result<IReadOnlyList<BlotterRow>>> handler, HttpContext http, CancellationToken cancellationToken, DateOnly? date = null, Guid? accountId = null, string? stateGroup = null, int page = 1)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetExecutionBlotterQuery(date, accountId, stateGroup, page), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error, http);
    }

    private static async Task<Results<Ok<IReadOnlyList<PositionRow>>, ProblemHttpResult>> GetPositionsAsync(
        IQueryHandler<GetExecutionPositionsQuery, Result<IReadOnlyList<PositionRow>>> handler, HttpContext http, CancellationToken cancellationToken, Guid? accountId = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetExecutionPositionsQuery(accountId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error, http);
    }

    private static async Task<Results<Ok<IReadOnlyList<ActivityRow>>, ProblemHttpResult>> GetActivityAsync(
        IQueryHandler<GetExecutionActivityQuery, Result<IReadOnlyList<ActivityRow>>> handler, HttpContext http, CancellationToken cancellationToken, DateOnly? date = null, string? kind = null, string? by = null, int page = 1)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetExecutionActivityQuery(date, kind, by, page), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error, http);
    }

    private static async Task<Ok<ExecutionNotices>> GetNoticesAsync(IQueryHandler<GetExecutionNoticesQuery, ExecutionNotices> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetExecutionNoticesQuery(), cancellationToken));
    }

    /// <summary>An unknown filter is 422 <c>execution.invalid</c> (the spec's word); every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == ExecutionScreenErrors.Invalid
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
