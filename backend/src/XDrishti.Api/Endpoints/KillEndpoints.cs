using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The kill proposals, their application, the owner's automatic setting and the re-enable of a paused cell (spec CF-12 part 12b, ADR 0219). The list is a read. Applying a proposal needs the owner's session and the CSRF token (it only lowers risk). The switch and the
/// re-enable raise or allow risk, so each needs a fresh password for THIS act (a missing one is 403 <c>auth.step_up_required</c>, a wrong one is the sign-in 401 with the lockout rules); the actor is built from the signed-in identity, never from a body. Nothing here places an order.
/// </summary>
internal sealed class KillEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/confidence").WithTags("Confidence");

        group.MapGet("/kill-proposals", ListAsync)
            .WithName("ListKillProposals")
            .WithSummary("The kill proposals, newest first (state proposed, applied or dismissed; strategy; limit 1 to 200, default 50), each with its rule, numbers, the cells it would pause and who applied it, with the automatic setting (default OFF) and the thresholds beside them. Read only. 400 paper.invalid")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/kill-proposals/{id:long}/apply", ApplyAsync)
            .WithName("ApplyKillProposal")
            .WithSummary("Apply a kill proposal as the owner: every cell it names that is still Paper or Active moves to Paused through the lifecycle, with a changelog row carrying the rule, the numbers and the settings hash. Needs the owner's session and the CSRF token. Applying twice changes nothing twice. It places no order. 404 confidence.proposal_not_found, 409 confidence.proposal_closed")
            .ProducesRejections(conflict: true);

        group.MapPut("/kill-settings", SwitchAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeKillSettings")
            .WithSummary("Switch confidence.kill.auto (OFF by default; no row means OFF) ON or OFF as the owner; appends an audited row with the owner as actor. ON lets the daily decay-monitor pause the cells of a strategy when a kill rule fires, recorded as actor auto with the setting row, never as an owner approval; the CF-09 failure boundary is never executed automatically. Needs a session, the CSRF token and the password in the body, checked on EVERY change. 403 auth.step_up_required (no password), 401 auth.invalid_credentials, 423 auth.locked, 400 confidence.reason_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);

        var cells = api.MapGroup("/learning/cells").WithTags("Learning");
        cells.MapPost("/{key}/re-enable", ReEnableAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ReEnableCell")
            .WithSummary("Re-enable a paused cell (Paused to Paper; the key is the URL-encoded cell key) as the owner. Never automatic and never to Active. Needs a session, the CSRF token and a fresh password in the body. When a kill proposal paused the cell, every rule that fired must hold now and none may fire, else 409 confidence.rule_not_cleared (the spec's rule_cleared problem, named to read as what it says); a rule that cannot be evaluated is not a pass. The answer always warns that the CF-11 gates (R and S) are not enforcing yet. 403 auth.step_up_required (no password), 401 auth.invalid_credentials, 423 auth.locked, 403 confidence.owner_only, 409 confidence.cell_not_paused, 400 confidence.reason_invalid, 422 learning.invalid")
            .ProducesRejections(conflict: true, notFound: false);
    }

    private static async Task<Results<Ok<KillProposalsView>, ProblemHttpResult>> ListAsync(
        string? state,
        string? strategy,
        int? limit,
        HttpContext http,
        IQueryHandler<GetKillProposalsQuery, Result<KillProposalsView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetKillProposalsQuery(string.IsNullOrWhiteSpace(state) ? null : state, strategy, limit ?? 50), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ApplyKillOutcome>, ProblemHttpResult>> ApplyAsync(
        long id,
        HttpContext http,
        ICommandHandler<ApplyKillCommand, ApplyKillOutcome> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // The safe direction: the owner of the session, no password. The actor never comes from a body.
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ApplyKillCommand(id, owner.Value), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<KillSwitched>, ProblemHttpResult>> SwitchAsync(
        KillSwitchRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeKillSwitchCommand, KillSwitched> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await FreshOwnerAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeKillSwitchCommand(owner.Value, request.Auto, request.Reason ?? string.Empty), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ReEnableOutcome>, ProblemHttpResult>> ReEnableAsync(
        string key,
        ReEnableRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ReEnableCellCommand, ReEnableOutcome> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await FreshOwnerAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ReEnableCellCommand(key, owner.Value, request.Reason ?? string.Empty), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return result.Error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Message, result.Error.Code))
            : ProblemResults.From(result.Error, http);
    }

    /// <summary>The owner after a fresh password for this act. No session is 401; a body without a password is 403 <c>auth.step_up_required</c> (nothing is checked or counted); a wrong one is the sign-in 401 or 423 with its lockout rules.</summary>
    private static async Task<Result<string>> FreshOwnerAsync(HttpContext http, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, string? password, CancellationToken cancellationToken)
    {
        var session = OwnerAct.SessionOwner(http);
        if (session.IsFailure)
        {
            return session;
        }

        if (string.IsNullOrEmpty(password))
        {
            return Result.Failure<string>(AuthErrors.StepUpRequired);
        }

        return await OwnerAct.AuthorizeAsync(http, passwordCheck, password, cancellationToken);
    }
}
