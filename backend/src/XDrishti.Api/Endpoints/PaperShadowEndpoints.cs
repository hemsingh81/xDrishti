using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Shadow;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The shadow record of every nightly candidate (spec CF-08 part B, ADR 0212): its summary, and the owner's switch. The summary is read only. The switch change needs a session, the CSRF token and a fresh password for THIS change, on and off alike (the same rule as the
/// autonomy switches, ADR 0091); the actor is built from the signed-in identity, never from the body. Nothing here places an order or moves a ticket.
/// </summary>
internal sealed class PaperShadowEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/paper/shadow").WithTags("Paper");

        group.MapGet("/summary", SummaryAsync)
            .WithName("GetShadowSummary")
            .WithSummary("The shadow record of every nightly candidate (written before the open, graded after the close by the paper replay): raw n and effective n with the correlation used (and whether it is the placeholder), the mean R with a 90% interval and the hit rate for all and by group (primary, reserve, rejected), the behavioural count of P10 and the edge count side by side, the pace once 10 trades are graded, the chain head (sessions, gaps, last digest) and the charge-sheet basis (gross unless the sheet is confirmed), and the sequential failure test line (CF-09: states of the edge and behavioural series and one text line; a flag, never a pass). Always says: modelled fills; no queue, no latency, and that this is a separate count (the P10 exit criterion is untouched). Says so when there are no rows. Read only; no order exists")
            .RequireAuthorization(ServiceReadAuthentication.AssistantEvidenceReadPolicy);

        group.MapPut("/settings", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeShadowSettings")
            .WithSummary("Switch the shadow record (paper.shadow.enabled, OFF by default) ON or OFF as the owner; appends an audited row. Needs a session, the CSRF token and the password in the body, checked on EVERY change (an earlier step-up window does not count and is not used up). ON only allows the services shadow-write and shadow-grade (which also start paused). 401 auth.invalid_credentials, 423 auth.locked, 403 paper.shadow_owner_only, 400 paper.shadow_reason_invalid, 409 paper.shadow_gap (turning ON over missed sessions without acknowledgeGap), 409 paper.shadow_digest_unwritable (turning ON while the digest folder cannot be written), 429 auth.rate_limited")
            .ProducesRejections(conflict: true, notFound: false)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/evidence-set", EvidenceSetAsync)
            .WithName("GetShadowEvidenceSet")
            .WithSummary("The frozen evidence set of the current edge epoch of the shadow record (spec AM-01 GAP-09, ADR 0282): the strategy versions whose shadow rows count for the edge count and the readiness forward rows, its state (pending: none stored, the first shadow write registers the default of the eligible cells' versions; open: stored, no shadow row of the epoch yet, the owner may replace it; frozen: never replaceable), who or what froze it (mode auto = system:default, or owner) and why. Rows of other versions are still written and graded, labelled outside_set, and left out of the edge figures. Read only (a session; not one of the assistant's evidence GETs); no order exists");

        group.MapPut("/evidence-set", ReplaceEvidenceSetAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ReplaceShadowEvidenceSet")
            .WithSummary("Replace the evidence set of the current edge epoch as the owner, before the first shadow write of the epoch and never after. Stores one audited row and registers the versions in the trial ledger (a registration, not a look). Needs a session, the CSRF token and the password in the body, checked on EVERY change. 401 auth.invalid_credentials, 423 auth.locked, 403 paper.evidence_set_owner_only, 400 paper.evidence_set_reason_invalid, 400 paper.evidence_set_versions_invalid, 400 paper.evidence_set_unknown_version, 409 paper.evidence_set_frozen (a shadow row of the epoch exists), 429 auth.rate_limited")
            .ProducesRejections(conflict: true, notFound: false)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<Ok<EvidenceSetView>> EvidenceSetAsync(IQueryHandler<GetEvidenceSetQuery, EvidenceSetView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetEvidenceSetQuery(), cancellationToken));

    private static async Task<Results<Ok<EvidenceSetReplaced>, ProblemHttpResult>> ReplaceEvidenceSetAsync(
        EvidenceSetRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ReplaceEvidenceSetCommand, EvidenceSetReplaced> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // A fresh password for this change (OwnerAct), then the owner from the session; nothing in the body can name the actor.
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ReplaceEvidenceSetCommand(owner.Value, request.Versions ?? [], request.Reason ?? string.Empty), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<ShadowSummaryView>> SummaryAsync(IQueryHandler<GetShadowSummaryQuery, ShadowSummaryView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetShadowSummaryQuery(), cancellationToken));

    private static async Task<Results<Ok<ShadowSwitched>, ProblemHttpResult>> ChangeAsync(
        ShadowSwitchRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeShadowSwitchCommand, ShadowSwitched> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // A fresh password for this change (OwnerAct: no step-up window opened, extended or used up), then the owner from the session; nothing in the body can name the actor.
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeShadowSwitchCommand(owner.Value, request.Enabled, request.Reason ?? string.Empty, request.AcknowledgeGap), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
