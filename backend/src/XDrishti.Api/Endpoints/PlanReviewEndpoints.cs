using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Planning;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner's review of a plan (spec P7-10, ADR 0095): read the review, approve, modify, reject, approve all, and the suggestions inbox. Every write needs a session and the CSRF token; the actor is built here from the signed-in session's e-mail address
/// (<c>owner:</c> + address) and a body can never supply or override it. Manual approval only: nothing here selects an automatic approval, a system actor or the rule actor. Approved means watched; no order is placed and none exists.
/// </summary>
internal sealed class PlanReviewEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/plans").WithTags("Plan review");

        group.MapGet("/{id:long}/review", ReviewAsync)
            .WithName("GetPlanReview")
            .WithSummary("The review of a plan: each primary and reserve ticket with its stored checklist per account (standing of every check, what needs the owner's confirmation, whether approve-all would take it), the cutoff and whether decisions are still open. Approved means watched; no order is placed and none exists. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:long}/items/{itemId:long}/approve", ApproveAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("ApprovePlanItem")
            .WithSummary("Approve one ticket as the owner (session and CSRF token; the owner is the session, never the body). Every account's stored checklist must allow it; a check that is not a pass needs a confirmation naming its code, the account and a reason of 1 to 200 characters; the account gate can never be overridden. 'Approved for watching: no order is placed and none exists.' 409 plans.checklist_blocked, plans.closed (cutoff passed), plans.move_refused or plans.stale (already decided), 400 checklist.* (confirmation), 404")
            .ProducesRejections(conflict: true);

        group.MapPost("/{id:long}/items/{itemId:long}/modify", ModifyAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("ModifyPlanItem")
            .WithSummary("Lower the size of a ticket for accounts it is already sized for (only reductions; nothing else can be edited). The ticket becomes Modified, not approved: approving it re-validates the change and the checklist. Refused after the cutoff. 400 plans.modification_invalid, 409 plans.closed, plans.move_refused, plans.checklist_blocked, 404")
            .ProducesRejections(conflict: true);

        group.MapPost("/{id:long}/items/{itemId:long}/reject", RejectAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("RejectPlanItem")
            .WithSummary("Reject one ticket as the owner. Allowed after the cutoff time until the cutoff job has closed the plan. 409 plans.move_refused or plans.stale, 404")
            .ProducesRejections(conflict: true);

        group.MapPost("/{id:long}/approve-all", ApproveAllAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("ApproveAllPlanItems")
            .WithSummary("Approve every primary, still-proposed ticket whose stored checklist is complete with every check passed on every account; tickets with a failed, warned or unavailable check, reserve and modified tickets are skipped and listed with the reason, never approved despite a check. 409 plans.closed, 404")
            .ProducesRejections(conflict: true);

        group.MapGet("/{id:long}/suggestions", SuggestionsAsync)
            .WithName("ListPlanSuggestions")
            .WithSummary("The suggestions inbox of a plan with the owner's seen marks. Advice only; it changes nothing. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:long}/suggestions/{suggestionId:long}/seen", SeenAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("MarkPlanSuggestionSeen")
            .WithSummary("Mark a suggestion as seen (an insert-only row; repeating it changes nothing). 404 plans.suggestion_not_found")
            .ProducesRejections(conflict: false);
    }

    // The owner is the signed-in session's e-mail address; with no verified session there is no owner (401). Nothing from the request body or query is read.
    private static string? OwnerActor(HttpContext http) =>
        http.GetVerifiedSession()?.Info.User.Email is { } email && !string.IsNullOrWhiteSpace(email) ? OwnerConfirmation.OwnerPrefix + email : null;

    private static async Task<Results<Ok<PlanReviewView>, ProblemHttpResult>> ReviewAsync(long id, HttpContext http, IQueryHandler<GetPlanReviewQuery, Result<PlanReviewView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPlanReviewQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ReviewDecisionOutcome>, ProblemHttpResult>> ApproveAsync(
        long id,
        long itemId,
        ApproveTicketRequest? request,
        HttpContext http,
        ICommandHandler<ApproveTicketCommand, ReviewDecisionOutcome> handler,
        CancellationToken cancellationToken)
    {
        if (OwnerActor(http) is not { } actor)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var confirmations = request?.Confirmations?.Select(c => new CheckConfirmation(c.AccountId, c.Codes ?? [], c.Reason ?? string.Empty)).ToList();
        var result = await handler.HandleAsync(new ApproveTicketCommand(id, itemId, actor, request?.Reason, confirmations), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ReviewDecisionOutcome>, ProblemHttpResult>> ModifyAsync(
        long id,
        long itemId,
        ModifyTicketRequest? request,
        HttpContext http,
        ICommandHandler<ModifyTicketCommand, ReviewDecisionOutcome> handler,
        CancellationToken cancellationToken)
    {
        if (OwnerActor(http) is not { } actor)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var quantities = request?.Quantities?.Select(q => new QuantityChange(q.AccountId, q.Quantity)).ToList() ?? [];
        var result = await handler.HandleAsync(new ModifyTicketCommand(id, itemId, actor, request?.Reason ?? string.Empty, quantities), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ReviewDecisionOutcome>, ProblemHttpResult>> RejectAsync(
        long id,
        long itemId,
        RejectTicketRequest? request,
        HttpContext http,
        ICommandHandler<RejectTicketCommand, ReviewDecisionOutcome> handler,
        CancellationToken cancellationToken)
    {
        if (OwnerActor(http) is not { } actor)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new RejectTicketCommand(id, itemId, actor, request?.Reason ?? string.Empty), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ApproveAllOutcome>, ProblemHttpResult>> ApproveAllAsync(long id, HttpContext http, ICommandHandler<ApproveAllCommand, ApproveAllOutcome> handler, CancellationToken cancellationToken)
    {
        if (OwnerActor(http) is not { } actor)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new ApproveAllCommand(id, actor), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<SuggestionView>>, ProblemHttpResult>> SuggestionsAsync(long id, HttpContext http, IQueryHandler<ListSuggestionsQuery, Result<IReadOnlyList<SuggestionView>>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListSuggestionsQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SeenAsync(long id, long suggestionId, HttpContext http, ICommandHandler<MarkSuggestionSeenCommand> handler, CancellationToken cancellationToken)
    {
        if (OwnerActor(http) is not { } actor)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new MarkSuggestionSeenCommand(id, suggestionId, actor), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.NoContent();
    }

    /// <summary>A refused input of the plan record is 422 like the other plan routes; the confirmation and modification errors are 400 validation; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == PlanErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
