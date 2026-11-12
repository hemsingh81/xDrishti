using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The readiness scorecard, per strategy and for the system, and the owner's enforce switch (spec CF-11 part 1, ADR 0221). The scorecards are derived reads and place no order. The switch raises how much the system refuses (a failed gate refuses the lifecycle move instead of
/// warning), and turning it OFF removes a refusal, which raises risk; so a change either way needs a fresh password for THIS act (a missing one is 403 <c>auth.step_up_required</c>, a wrong one is the sign-in 401 with the lockout rules), like <c>confidence.kill.auto</c>; the
/// actor is built from the signed-in identity, never from a body. Nothing here moves a cell: the gates are called by the owner's lifecycle move.
/// </summary>
internal sealed class ReadinessEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/confidence").WithTags("Confidence");

        group.MapGet("/scorecards", ListAsync)
            .WithName("ListScorecards")
            .WithSummary("The readiness system card (the minimum of the integrity and the weighted mean of the strategy scores, equal weights labelled when the plan exposes no risk share) and one line per Lab strategy at its latest version, weakest first: score, label, weakest sub-score, rows passed of 15, hard fails, gates R and S. Derived on every read, no order. An input that does not exist is unavailable, which is never a pass.")
            .RequireAuthorization(ServiceReadAuthentication.AssistantEvidenceReadPolicy)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/scorecards/{strategyId}/versions/{version:int}", GetAsync)
            .WithName("GetScorecard")
            .WithSummary("The scorecard of one strategy version: the 15-row edge checklist (value, threshold, producer, pass / fail / unavailable), the six sub-scores with what would move each, the hard fails by name, the label, gates R and S with the rows that stop them, what the card is waiting for, the settings hash and the newest daily snapshots. Derived on every read. 400 confidence.readiness_invalid, 404 confidence.scorecard_not_found")
            .RequireAuthorization(ServiceReadAuthentication.AssistantEvidenceReadPolicy)
            .ProducesRejections(conflict: false);

        group.MapGet("/readiness-settings", GetSettingsAsync)
            .WithName("GetReadinessSettings")
            .WithSummary("The readiness enforce switch (OFF when no row is stored) and the text and hash of the thresholds. Read only.");

        group.MapPut("/readiness-settings", SwitchAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeReadinessSettings")
            .WithSummary("Switch confidence.readiness.enforce (OFF by default; no row means OFF) as the owner; appends an audited row with the owner as actor. ON makes a failed readiness gate refuse the cell move it judges (Candidate to Paper, the owner's re-enable of a paused cell, Paper to Active) with 409 confidence.gate_failed and the failing rows; OFF only warns and records the result. Needs a session, the CSRF token and the password in the body, checked on EVERY change, ON and OFF alike (OFF removes a refusal). 403 auth.step_up_required (no password), 401 auth.invalid_credentials, 423 auth.locked, 400 confidence.reason_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapPost("/pack-gate-results", RecordPackGateAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("RecordPackGateResult")
            .WithSummary("Record, as the owner, the result of a verify-pack --gate run for a strategy version (insert only; the owner is the actor). Row 15's P3-09 20-trade term reads the newest record: PASS over at least 20 trades is a pass, any other status is a fail, no record is unavailable (a fail). Needs a session, the CSRF token and the password in the body. 403 auth.step_up_required, 401 auth.invalid_credentials, 423 auth.locked, 400 confidence.readiness_invalid or confidence.reason_invalid, 404 confidence.scorecard_not_found, 429 auth.rate_limited")
            .ProducesRejections(conflict: false);
    }

    private static async Task<Results<Ok<ScorecardListView>, ProblemHttpResult>> ListAsync(
        HttpContext http,
        IQueryHandler<ListScorecardsQuery, Result<ScorecardListView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListScorecardsQuery(), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ScorecardView>, ProblemHttpResult>> GetAsync(
        string strategyId,
        int version,
        HttpContext http,
        IQueryHandler<GetScorecardQuery, Result<ScorecardView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetScorecardQuery(strategyId, version), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<ReadinessSettingsView>> GetSettingsAsync(
        IQueryHandler<GetReadinessSettingsQuery, ReadinessSettingsView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetReadinessSettingsQuery(), cancellationToken));

    private static async Task<Results<Ok<ReadinessSwitched>, ProblemHttpResult>> SwitchAsync(
        ReadinessSwitchRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeReadinessSwitchCommand, ReadinessSwitched> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // Both directions need a fresh password: turning enforcement OFF removes a refusal, which raises risk (like confidence.kill.auto), and ON changes what the owner's own moves meet.
        var owner = await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeReadinessSwitchCommand(owner.Value, request.Enforce, request.Reason ?? string.Empty), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PackGateRecorded>, ProblemHttpResult>> RecordPackGateAsync(
        PackGateResultRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<RecordPackGateResultCommand, PackGateRecorded> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(
            new RecordPackGateResultCommand(owner.Value, request.StrategyId ?? string.Empty, request.Version, request.PackId ?? string.Empty, request.Status ?? string.Empty, request.TradeCount, request.Reason ?? string.Empty),
            cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
