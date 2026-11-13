using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Promotion, rollback and the ML switches (spec CF-10 D7 and D8, part 10d-1, ADR 0220). The list is a read. Applying a rollback proposal needs the owner's session and the CSRF token (it only lowers reliance on ML). Promoting a challenger and turning
/// <c>ml.influence.enabled</c> or <c>ml.auto_promote</c> ON raise how far a model may matter, so each needs a fresh password for THIS act (a missing one is 403 <c>auth.step_up_required</c>, a wrong one is the sign-in 401 with the lockout rules); the actor is built from the signed-in
/// identity, never from a body. Nothing here places an order, and no route re-promotes anything but at the owner's request.
/// </summary>
internal sealed class MlSafetyEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var learning = api.MapGroup("/learning").WithTags("Learning");

        learning.MapGet("/rollback/proposals", ListAsync)
            .WithName("ListRollbackProposals")
            .WithSummary("The rollback proposals, newest first (state proposed, applied or dismissed; modelId; limit 1 to 200, default 50), each with its trigger (the P6-09 calibration detector or rolling band detector), numbers, whether the previous champion still qualifies and who applied it, with the three ML switches (all default OFF) beside them. Read only. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        learning.MapPost("/rollback/{id:long}/apply", ApplyAsync)
            .WithName("ApplyRollbackProposal")
            .WithSummary("Apply a rollback proposal as the owner: the champion moves to challenger (shadow) through the registry's designation, with a changelog row carrying the trigger, the numbers and the settings hash. Needs the owner's session and the CSRF token (it lowers reliance on ML). Applying twice changes nothing twice. It never re-promotes the previous champion: there is no champion until the owner names one. 404 learning.rollback_not_found, 409 learning.rollback_closed")
            .ProducesRejections(conflict: true);

        learning.MapPut("/ml-settings", SwitchAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password may be checked here, so the sign-in limit applies
            .WithName("ChangeMlSettings")
            .WithSummary("Change the ML switches ml.influence.enabled, ml.auto_promote and ml.auto_rollback (all OFF by default; no row means OFF) as the owner; each change appends an audited row with the owner as actor. A switch that is not given is left alone. Changing ml.holdout.auto_score (ON by default, ADR 0282) in either direction also needs the password. Turning ml.influence.enabled or ml.auto_promote ON needs the session, the CSRF token and the password in the body, checked on EVERY such change (403 auth.step_up_required with no password, 401 auth.invalid_credentials, 423 auth.locked); ml.auto_rollback, and turning any switch OFF, needs the session and the CSRF token only. ON opens nothing alone: influence still needs the predicate's evidence terms, an automatic rollback is recorded as actor auto with the setting row, never as an owner approval, and an automatic promotion needs the promotion rule and the predicate without the switch. 400 learning.ml_settings_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);

        learning.MapPost("/models/{id:long}/promote", PromoteAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("PromoteModel")
            .WithSummary("Promote a model to champion as the owner, with the P6-08 changelog row. A challenger needs 30 graded candidates over 4 weeks, the paired bootstrap against the champion at 0.95, a P6-12 counterfactual that is not FrozenBetter and the champion rule; a retired previous champion is re-promoted only if its latest evaluation still satisfies the predicate without the owner's switch. A figure that is not available is never a pass (the sources of the first three do not exist yet, so every promotion is refused with the reason). Promotion never turns ml.influence.enabled on. Needs a session, the CSRF token and a fresh password in the body. 403 auth.step_up_required (no password), 401 auth.invalid_credentials, 423 auth.locked, 404 models.not_found, 409 learning.promotion_refused or learning.repromotion_refused or models.not_eligible, 422 learning.invalid")
            .ProducesRejections(conflict: true);

        learning.MapPost("/models/{id:long}/holdout-score", ScoreHoldOutAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ScoreModelHoldOut")
            .WithSummary("Score a model on the locked hold-out as the owner (CF-10e part 3, ADR 0285): claims the model family's one fresh look at the block in force (insert-only, burned even if nothing can be scored), runs the model's strategy versions once over the block and stores the figures (rows, log loss against the better no-ML baseline, the paired day-cluster bootstrap probability and interval, and the effect of the fixed take rule on realised R). The evening seal service does the same, recorded as auto, while ml.holdout.auto_score is on. Needs a session, the CSRF token and a fresh password in the body (403 auth.step_up_required, 401 auth.invalid_credentials, 423 auth.locked). 404 models.not_found, 409 learning.holdout_reused (this family, or another model, already took the model slot of this block) or learning.holdout_unavailable (no block, model not kept out of the block, not registered before the block, or inputs missing), 422 learning.holdout_no_rows")
            .ProducesRejections(conflict: true);
    }

    private static async Task<Results<Ok<MlHoldOutScoreView>, ProblemHttpResult>> ScoreHoldOutAsync(
        long id,
        MlHoldOutScoreRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ScoreModelHoldOutCommand, MlHoldOutScoreView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ScoreModelHoldOutCommand(id, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<RollbackProposalsView>, ProblemHttpResult>> ListAsync(
        string? state,
        long? modelId,
        int? limit,
        HttpContext http,
        IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetRollbackProposalsQuery(string.IsNullOrWhiteSpace(state) ? null : state, modelId, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ApplyRollbackOutcome>, ProblemHttpResult>> ApplyAsync(
        long id,
        HttpContext http,
        ICommandHandler<ApplyRollbackCommand, ApplyRollbackOutcome> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // The safe direction: the owner of the session, no password. The actor never comes from a body.
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ApplyRollbackCommand(id, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<MlSettingsChanged>, ProblemHttpResult>> SwitchAsync(
        MlSettingsRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeMlSettingsCommand, MlSettingsChanged> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // raising how far a model may matter needs the password for THIS change; lowering it, and the automatic rollback, need the session only
        var owner = MlSwitch.NeedsFreshPassword(request.InfluenceEnabled, request.AutoPromote, request.AutoHoldOutScore)
            ? await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken)
            : OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeMlSettingsCommand(owner.Value, request.InfluenceEnabled, request.AutoPromote, request.AutoRollback, request.Reason ?? string.Empty, request.AutoHoldOutScore), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PromotionOutcome>, ProblemHttpResult>> PromoteAsync(
        long id,
        PromoteModelRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<PromoteChallengerCommand, PromotionOutcome> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new PromoteChallengerCommand(id, owner.Value, request.Reason ?? string.Empty), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code is LearningErrors.InvalidCode or MlInfluenceErrors.HoldOutNoRowsCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
