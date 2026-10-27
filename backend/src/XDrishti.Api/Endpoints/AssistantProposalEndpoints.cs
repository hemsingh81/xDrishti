using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai.Proposals;
using XDrishti.Application.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The assistant's proposals (spec P9-08, ADR 0142). The model only writes inbox rows; here the owner reads them and decides. Accept needs the session, the CSRF token, the switch <c>allow_proposal_accept</c> (asked FIRST, so that a switched-off act neither counts toward the sign-in
/// lockout nor says anything about the password) and the owner's fresh password on every call. Reject needs the session only (the safe direction). The actor is always the signed-in owner, never from a body. Reads work while generation is off.
/// </summary>
internal sealed class AssistantProposalEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/assistant/proposals").WithTags("Assistant");

        group.MapGet(string.Empty, ListAsync)
            .WithName("ListAssistantProposals")
            .WithSummary("The assistant's proposals, newest first (limit 1 to 100, default 50), optionally of one state (pending, accepted, rejected, expired) and kind (hypothesis, basket_add, basket_remove). A pending proposal past its expiry is shown as expired. Read only. 422 assistant.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:long}", GetAsync)
            .WithName("GetAssistantProposal")
            .WithSummary("One proposal with its evidence, the engine's checks and its decision. Read only. 404 assistant.proposal_not_found")
            .ProducesRejections(conflict: false);

        group.MapPost("/{id:long}/accept", AcceptAsync)
            .WithName("AcceptAssistantProposal")
            .WithSummary("The owner's accept: runs every engine check again, then writes a proposed hypothesis or changes a basket (never tracks an instrument, starts a backfill, approves or runs anything, or places an order). Needs a fresh password in the body and the switch allow_proposal_accept. A second accept returns the first result. 409 assistant.capability_off, assistant.proposal_closed, assistant.proposal_stale, hypotheses.open_limit")
            .ProducesRejections(conflict: true);

        group.MapPost("/{id:long}/reject", RejectAsync)
            .WithName("RejectAssistantProposal")
            .WithSummary("The owner rejects a proposal (no password: the safe direction); the same idea is not proposed again for 30 days. A second reject returns the first result. 409 assistant.proposal_closed; 422 assistant.invalid")
            .ProducesRejections(conflict: true)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<IReadOnlyList<ProposalView>>, ProblemHttpResult>> ListAsync(
        HttpContext http, IQueryHandler<ListProposalsQuery, Result<IReadOnlyList<ProposalView>>> handler, CancellationToken cancellationToken, string? state = null, string? kind = null, int limit = ListProposalsQuery.DefaultLimit)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new ListProposalsQuery(state, kind, limit), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ProposalView>, ProblemHttpResult>> GetAsync(
        long id, HttpContext http, IQueryHandler<GetProposalQuery, Result<ProposalView>> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetProposalQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ProposalView>, ProblemHttpResult>> AcceptAsync(
        long id,
        AcceptProposalRequest request,
        HttpContext http,
        IQueryHandler<ProposalAcceptAllowedQuery, bool> allowed,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<AcceptProposalCommand, ProposalView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await allowed.HandleAsync(new ProposalAcceptAllowedQuery(), cancellationToken))
        {
            return Problem(ProposalErrors.CapabilityOff(), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new AcceptProposalCommand(id, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ProposalView>, ProblemHttpResult>> RejectAsync(
        long id, RejectProposalRequest request, HttpContext http, ICommandHandler<RejectProposalCommand, ProposalView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new RejectProposalCommand(id, request.Reason, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422 as the contract says; the rest through the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind != ErrorKind.Validation ? ProblemResults.From(error, http) : TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code));
}
