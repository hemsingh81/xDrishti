using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Application.Auth;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Strategy drafting (spec P9-07, ADR 0141). A draft is text the model wrote for the owner to read: starting one runs the model job only; nothing is stored as a strategy and nothing is run until the owner confirms. Confirm needs the session, the CSRF token, the switch
/// <c>allow_draft_confirm</c> (asked FIRST, so that a switched-off act neither counts toward the sign-in lockout nor says anything about the password) and the owner's fresh password on every call. The actor is always the signed-in owner, never from a body.
/// </summary>
internal sealed class AssistantDraftEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/assistant/strategy-drafts").WithTags("Assistant");

        group.MapPost(string.Empty, StartAsync)
            .WithName("StartAssistantStrategyDraft")
            .WithSummary("The owner types a trading idea (10 to 2,000 characters); starts the model job and answers 202 with the draft in state drafting (poll GET). Stores nothing as a strategy and runs nothing. 409 assistant.disabled, assistant.limit, assistant.busy; 422 assistant.invalid")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet(string.Empty, ListAsync)
            .WithName("ListAssistantStrategyDrafts")
            .WithSummary("The newest finished drafts (limit 1 to 50, default 20) with their decisions. Read only. 422 assistant.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:long}", GetAsync)
            .WithName("GetAssistantStrategyDraft")
            .WithSummary("One draft: drafting while its job runs, then valid, invalid, declined or failed, and confirmed or discarded after the owner's decision; the linked quick test's deterministic summary once it has a state. Read only. 404 assistant.draft_not_found")
            .ProducesRejections(conflict: false);

        group.MapPost("/{id:long}/confirm", ConfirmAsync)
            .WithName("ConfirmAssistantStrategyDraft")
            .WithSummary("The owner's confirm: re-validates the text sent, stores it as a normal library version and, when run is true, starts a quick test on the universe the owner picked. Needs a fresh password in the body and the switch allow_draft_confirm. 409 assistant.capability_off, assistant.draft_id_taken, assistant.draft_discarded; 422 assistant.draft_invalid, assistant.invalid, lab.invalid_strategy")
            .ProducesRejections(conflict: true)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:long}/discard", DiscardAsync)
            .WithName("DiscardAssistantStrategyDraft")
            .WithSummary("The owner discards a draft (no password: the safe direction). A confirmed draft cannot be discarded. 409 assistant.draft_confirmed")
            .ProducesRejections(conflict: true);
    }

    private static async Task<Results<Accepted<DraftView>, ProblemHttpResult>> StartAsync(
        StartDraftRequest request, HttpContext http, ICommandHandler<StartDraftCommand, DraftView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new StartDraftCommand(request.Idea ?? string.Empty, request.BasketId, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted($"/api/assistant/strategy-drafts/{result.Value.Id}", result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<DraftView>>, ProblemHttpResult>> ListAsync(
        HttpContext http, IQueryHandler<ListDraftsQuery, Result<IReadOnlyList<DraftView>>> handler, CancellationToken cancellationToken, int limit = ListDraftsQuery.DefaultLimit)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new ListDraftsQuery(limit), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<DraftView>, ProblemHttpResult>> GetAsync(
        long id, HttpContext http, IQueryHandler<GetDraftQuery, Result<DraftView>> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetDraftQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<DraftView>, ProblemHttpResult>> ConfirmAsync(
        long id,
        ConfirmDraftRequest request,
        HttpContext http,
        IQueryHandler<DraftConfirmAllowedQuery, bool> allowed,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ConfirmDraftCommand, DraftView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await allowed.HandleAsync(new DraftConfirmAllowedQuery(), cancellationToken))
        {
            return Problem(DraftErrors.CapabilityOff(), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ConfirmDraftCommand(id, request.Text, request.BasketId, request.Symbols, request.Run, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<DraftView>, ProblemHttpResult>> DiscardAsync(
        long id, HttpContext http, ICommandHandler<DiscardDraftCommand, DraftView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new DiscardDraftCommand(id, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422 as the contract says (with the located problems of a refused text); the rest through the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http)
    {
        if (error.Kind != ErrorKind.Validation)
        {
            return ProblemResults.From(error, http);
        }

        var details = ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code);
        if (error.Code == LabErrors.InvalidStrategyCode && error.Payload is { } payload && payload.TryGetValue(LabErrors.ProblemsKey, out var problems))
        {
            details.Extensions[LabErrors.ProblemsKey] = (IReadOnlyList<StrategyIssue>)problems;
        }

        return TypedResults.Problem(details);
    }
}
