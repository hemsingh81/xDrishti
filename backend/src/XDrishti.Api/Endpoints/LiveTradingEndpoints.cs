using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The compliance record of the small live phase (spec P11-05, ADR 0147): read the owner's dated notes about the static IP, the SEBI re-check and Dhan's IP rule for reads, and write one. The write needs a session, the CSRF token, the deployment gate
/// <c>Live:Writes:Enabled</c> (OFF by default) and a fresh password for THIS save (the step-up window is not used); the actor is built from the signed-in identity, never from the body. Nothing here calls a broker or an ISP, and no route has an order segment.
/// </summary>
internal sealed class LiveTradingEndpoints : IEndpointModule
{
    private const int DefaultLimit = 50;

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/live-trading").WithTags("Live trading");

        group.MapGet("/compliance", GetAsync)
            .WithName("GetLiveCompliance")
            .WithSummary("The compliance records, newest first (kind filters; limit 1 to 200, default 50), with the newest record of each kind (null = nothing recorded for that kind, which is empty and not 'no change') and whether the web may write now. The overdue flag is set on the newest record of a kind whose review date has passed. 422 live.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/compliance", RecordAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("RecordLiveCompliance")
            .WithSummary("Write one compliance record as the owner; insert-only and audited (kind, outcome, decided date; never the text). Needs a session, the CSRF token, the deployment gate Live:Writes:Enabled (off by default) and the password in the body, checked on EVERY save (an earlier step-up window does not count and is not used up). The record is your note of what you were told: nothing is checked at Dhan or the ISP. 403 live.capability_off, 401 auth.invalid_credentials, 423 auth.locked, 403 live.owner_only, 422 live.invalid (a field that looks like an IP address, client id or token, or a rule of the record), 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/ramp", GetRampAsync)
            .WithName("GetLiveRamp")
            .WithSummary("The ramp of a live account: step (null = none, so no live allocation), the scale and the live risk it gives, hold, the evidence gates G1 to G7 (state, number, threshold, n; only pass counts), the breach rules, today's loss, the halts and the history. 404 live.account_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/ramp/steps", SetRampStepAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("SetLiveRampStep")
            .WithSummary("Set the ramp step as the owner: S1 first, or lower. Insert-only and audited. Needs a session, the CSRF token, the deployment gate Live:Writes:Enabled (off by default) and the password in the body, checked on EVERY move. A raise is 409 live.gates_not_met until the evidence gates exist; 409 live.step_not_adjacent, 409 live.stale, 403 live.capability_off, 401 auth.invalid_credentials, 423 auth.locked, 422 live.invalid, 429 auth.rate_limited. Nothing here places an order")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/ramp/clear-hold", ClearHoldAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ClearLiveRampHold")
            .WithSummary("Clear a hold of the ramp as the owner. Refused with 409 live.hold_condition_active while a breach condition still holds on the evidence read now (event breaches need the cooldown sessions without a new one). Insert-only and audited. Same gates as the step: session, CSRF, Live:Writes:Enabled and the password in the body, checked on this call.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/ramp/exit-confirmed", ConfirmExitAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ConfirmLiveRampExit")
            .WithSummary("Record as the owner that the small-live exit report was read and is met, the key to S3. Refused with 409 live.exit_not_checked unless that report says MET right now; the report (P11-06) is not built yet, so it is always refused for now. Same gates as the step.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/halts", GetHaltsAsync)
            .WithName("ListLiveHalts")
            .WithSummary("The halts of the live accounts (daily and weekly loss limits reached), newest first: the period, the limit, what was measured under which state (value, realised_only, unavailable) and what the halt does. A halt is advice: you place every order. accountId optional; from and to default to the last 30 days, at most 92.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/notices", GetNoticesAsync)
            .WithName("ListLiveNotices")
            .WithSummary("The derived notices of the live record: a halt in force, a hold, a capture gap, a step the evidence has earned, a compliance re-check overdue. Words and a route only; nothing to run.");

        group.MapGet("/accounts/{accountId:guid}/capture", GetCaptureAsync)
            .WithName("GetLiveCapture")
            .WithSummary("The capture of one session of a manual-live account (date defaults to today, never the future): health (healthy, gap, not_run), the runs, one row per approved ticket (figures captured so far; the outcome once the settle has written it, null before), the fills no ticket owns and the owner's own trades, and the evidence. Every order-dependent field is 'unavailable' when the session has no order evidence, never a number. Read only. 404 live.not_found, 422 live.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/outcomes", ListOutcomesAsync)
            .WithName("ListLiveOutcomes")
            .WithSummary("The stored outcomes of live tickets (filled, partial, over_allocation, not_triggered, missed_no_order, missed_unfilled, rejected, missed_unknown, unknown), newest session first. accountId optional; from and to default to the last 30 days (range at most 92 days); page from 1, size 1 to 200. Read only. 422 live.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/executions", ListExecutionsAsync)
            .WithName("ListLiveExecutions")
            .WithSummary("The live entries measured against what the plan promised, newest session first: bucket, reference kind, fill VWAP and quantity, and the slippage in rupees, ticks, basis points and R split into the delay before the order and the execution itself (positive is worse for you; a figure with no input is 'unavailable', never 0). accountId optional; from and to default to the last 30 days (range at most 92 days); page from 1, size 1 to 200. Read only. 422 live.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/slippage", GetSlippageAsync)
            .WithName("GetLiveSlippage")
            .WithSummary("The stored slippage estimates of a week (a Monday; default the newest stored): one row per liquidity bucket, side, style and reference kind with its n; a row under the minimum n is 'insufficient' and has no mean. 'empty' when nothing is stored. Nothing is applied from them. Read only. 422 live.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/capacity", GetCapacityAsync)
            .WithName("GetLiveCapacity")
            .WithSummary("The stored capacity scores of a week: the share of entry orders filled in full for the account and for each liquidity bucket, with a credible interval, 'insufficient' below the minimum counted orders. Display only. Read only. 422 live.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/waterfall", GetWaterfallAsync)
            .WithName("GetLiveWaterfall")
            .WithSummary("The waterfall of one round trip by the id of its execution: plan result, entry slippage, exit difference, actual gross, and charges and actual net only when every fill's charges are recorded. 'unavailable' with the reason when the position did not close in the session or the plan was not graded. Read only. 404 live.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/settings", GetSettingsAsync)
            .WithName("GetLiveSettings")
            .WithSummary("The live switches (live.auto_approve_tickets, live.auto_ramp_up), both OFF when nothing is stored, with their history. No setting lets this system place an order");

        group.MapPut("/settings", ChangeSettingsAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ChangeLiveSettings")
            .WithSummary("Change the live switches as the owner; appends an audited row (the database stamps the hash). Same gates as the ramp step: session, CSRF, Live:Writes:Enabled and the password on every change. 409 live.stale, 403 live.capability_off, 401 auth.invalid_credentials, 423 auth.locked, 422 live.invalid, 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Results<Ok<LiveCaptureView>, ProblemHttpResult>> GetCaptureAsync(
        IQueryHandler<LiveCaptureQuery, Result<LiveCaptureView>> handler, HttpContext http, Guid accountId, DateOnly? date, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LiveCaptureQuery(accountId, date), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LiveOutcomesPage>, ProblemHttpResult>> ListOutcomesAsync(
        IQueryHandler<LiveOutcomesQuery, Result<LiveOutcomesPage>> handler, HttpContext http, Guid? accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken, int page = 1, int size = 50)
    {
        var result = await handler.HandleAsync(new LiveOutcomesQuery(accountId, from, to, page, size), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LiveExecutionsPage>, ProblemHttpResult>> ListExecutionsAsync(
        IQueryHandler<LiveExecutionsQuery, Result<LiveExecutionsPage>> handler, HttpContext http, Guid? accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken, int page = 1, int size = 50)
    {
        var result = await handler.HandleAsync(new LiveExecutionsQuery(accountId, from, to, page, size), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LiveSlippageView>, ProblemHttpResult>> GetSlippageAsync(
        IQueryHandler<LiveSlippageQuery, Result<LiveSlippageView>> handler, HttpContext http, DateOnly? weekStart, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LiveSlippageQuery(weekStart), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LiveCapacityView>, ProblemHttpResult>> GetCapacityAsync(
        IQueryHandler<LiveCapacityQuery, Result<LiveCapacityView>> handler, HttpContext http, DateOnly? weekStart, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LiveCapacityQuery(weekStart), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LiveWaterfallView>, ProblemHttpResult>> GetWaterfallAsync(
        IQueryHandler<LiveWaterfallQuery, Result<LiveWaterfallView>> handler, HttpContext http, long entryId, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LiveWaterfallQuery(entryId), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<RampView>, ProblemHttpResult>> GetRampAsync(IQueryHandler<GetRampQuery, Result<RampView>> handler, HttpContext http, Guid accountId, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetRampQuery(accountId), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<RampView>, ProblemHttpResult>> SetRampStepAsync(
        SetRampStepRequest request,
        HttpContext http,
        ILiveWritesGate gate,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<SetRampStepCommand, RampView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!gate.Enabled)
        {
            return Problem(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new SetRampStepCommand(owner.Value, request.AccountId, request.ToStep, request.Reason), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<RampView>, ProblemHttpResult>> ClearHoldAsync(
        RampActRequest request, HttpContext http, ILiveWritesGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<ClearHoldCommand, RampView> handler, CancellationToken cancellationToken) =>
        await OwnerRampActAsync(request, http, gate, passwordCheck, owner => handler.HandleAsync(new ClearHoldCommand(owner, request.AccountId, request.Reason), cancellationToken), cancellationToken);

    private static async Task<Results<Ok<RampView>, ProblemHttpResult>> ConfirmExitAsync(
        RampActRequest request, HttpContext http, ILiveWritesGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<ConfirmExitCommand, RampView> handler, CancellationToken cancellationToken) =>
        await OwnerRampActAsync(request, http, gate, passwordCheck, owner => handler.HandleAsync(new ConfirmExitCommand(owner, request.AccountId, request.Reason), cancellationToken), cancellationToken);

    /// <summary>The order of every owner write: the capability gate first (a switched-off save neither counts toward the sign-in lockout nor says anything about the password), then the fresh password for THIS act, then the owner from the session.</summary>
    private static async Task<Results<Ok<RampView>, ProblemHttpResult>> OwnerRampActAsync(
        RampActRequest request, HttpContext http, ILiveWritesGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, Func<string, ValueTask<Result<RampView>>> act, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!gate.Enabled)
        {
            return Problem(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await act(owner.Value);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<LiveHaltView>>, ProblemHttpResult>> GetHaltsAsync(
        IQueryHandler<GetLiveHaltsQuery, Result<IReadOnlyList<LiveHaltView>>> handler, HttpContext http, Guid? accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLiveHaltsQuery(accountId, from, to), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<IReadOnlyList<LiveNoticeView>>> GetNoticesAsync(IQueryHandler<GetLiveNoticesQuery, IReadOnlyList<LiveNoticeView>> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetLiveNoticesQuery(), cancellationToken));

    private static async Task<Ok<LiveSettingsView>> GetSettingsAsync(IQueryHandler<GetLiveSettingsQuery, LiveSettingsView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetLiveSettingsQuery(), cancellationToken));

    private static async Task<Results<Ok<LiveSettingsView>, ProblemHttpResult>> ChangeSettingsAsync(
        ChangeLiveSettingsRequest request,
        HttpContext http,
        ILiveWritesGate gate,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeLiveSettingsCommand, LiveSettingsView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!gate.Enabled)
        {
            return Problem(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeLiveSettingsCommand(owner.Value, request.Reason, request.AutoApproveTickets, request.AutoRampUp, request.ExpectedSettingsId), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ComplianceView>, ProblemHttpResult>> GetAsync(
        IQueryHandler<GetComplianceQuery, Result<ComplianceView>> handler,
        HttpContext http,
        CancellationToken cancellationToken,
        string? kind = null,
        int limit = DefaultLimit)
    {
        var result = await handler.HandleAsync(new GetComplianceQuery(string.IsNullOrWhiteSpace(kind) ? null : kind, limit), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Created<ComplianceRecordView>, ProblemHttpResult>> RecordAsync(
        RecordComplianceRequest request,
        HttpContext http,
        ILiveWritesGate gate,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<RecordComplianceCommand, ComplianceRecordView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // The gate first (a switched-off save neither counts toward the sign-in lockout nor says anything about the password), then a fresh password for this save, then the owner from the session.
        if (!gate.Enabled)
        {
            return Problem(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(
            new RecordComplianceCommand(owner.Value, request.Kind, request.Outcome, request.DecidedOn, request.Summary, request.Sources, request.StaticIpState, request.Isp, request.MonthlyCost, request.IpVersion, request.WhitelistedAt, request.NextReviewOn),
            cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Created("/api/live-trading/compliance?limit=1", result.Value);
    }

    /// <summary>A refused input of the record is 422, as in the Lab and the owner moves; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == ComplianceRecord.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
