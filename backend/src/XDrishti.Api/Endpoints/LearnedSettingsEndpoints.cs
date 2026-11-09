using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.LearnedSettings;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The body of an approve or an undo: the owner's password, re-entered for THIS act. Needed when the act loosens a gate (a loosening proposal, and any undo of a tightening); ignored otherwise. The owner is never part of the body.</summary>
internal sealed record LearnedPasswordRequest(string? Password);

/// <summary>
/// Learned setting changes (AU-10, ADR 0242, ADR 0247): the log, and the owner's decisions. A change toward the safer side is applied by itself only when Auto is ON; every other direction, and the undo of an automatic tightening, is applied only by the owner with a fresh
/// password for that act (checked on every request; an earlier step-up window does not count). The actor is built from the signed-in session. Nothing here places an order.
/// </summary>
internal sealed class LearnedSettingsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/settings/learned").WithTags("Settings");

        group.MapGet("/", GetAsync)
            .WithName("GetLearnedSettingChanges")
            .WithSummary("The learned changes of settings (newest first) with the Auto switch, the weekly cap and what it has used, the allow-list, and for each change whether approving or undoing it needs the owner's password");

        group.MapPost("/{id:long}/approve", ApproveAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ApproveLearnedSettingChange")
            .WithSummary("Apply a proposed change as the owner. A loosening needs the password in the body (403 auth.step_up_required without it, 401 auth.invalid_credentials, 423 auth.locked); a tightening needs only the session. 404 learned.not_found, 409 learned.wrong_state or learned.stale, 422-class learned.config_invalid when the whole configuration with it would be invalid")
            .ProducesRejections(conflict: true);

        group.MapPost("/{id:long}/dismiss", DismissAsync)
            .WithName("DismissLearnedSettingChange")
            .WithSummary("Set a proposed change aside as the owner (the safe direction: no password). 404 learned.not_found, 409 learned.wrong_state")
            .ProducesRejections(conflict: true);

        group.MapPost("/{id:long}/undo", UndoAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("UndoLearnedSettingChange")
            .WithSummary("Undo an applied change in one step. Undoing a tightening is a loosening: it needs the password in the body, at any Auto state. 404 learned.not_found, 409 learned.wrong_state or learned.stale, 403 auth.step_up_required")
            .ProducesRejections(conflict: true);
    }

    private static async Task<Ok<LearnedChangesView>> GetAsync(IQueryHandler<GetLearnedChangesQuery, LearnedChangesView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetLearnedChangesQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<LearnedChangeView>, ProblemHttpResult>> ApproveAsync(
        long id,
        LearnedPasswordRequest? request,
        HttpContext http,
        IQueryHandler<GetLearnedChangeQuery, LearnedChangeView?> read,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ApproveLearnedChangeCommand, LearnedChangeView> handler,
        CancellationToken cancellationToken) =>
        await DecideAsync(id, request, http, read, passwordCheck, v => v.ApproveNeedsPassword, (actor, fresh) => handler.HandleAsync(new ApproveLearnedChangeCommand(id, actor, fresh), cancellationToken), cancellationToken);

    private static async Task<Results<Ok<LearnedChangeView>, ProblemHttpResult>> UndoAsync(
        long id,
        LearnedPasswordRequest? request,
        HttpContext http,
        IQueryHandler<GetLearnedChangeQuery, LearnedChangeView?> read,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<UndoLearnedChangeCommand, LearnedChangeView> handler,
        CancellationToken cancellationToken) =>
        await DecideAsync(id, request, http, read, passwordCheck, v => v.UndoNeedsPassword, (actor, fresh) => handler.HandleAsync(new UndoLearnedChangeCommand(id, actor, fresh), cancellationToken), cancellationToken);

    private static async Task<Results<Ok<LearnedChangeView>, ProblemHttpResult>> DismissAsync(
        long id,
        HttpContext http,
        ICommandHandler<DismissLearnedChangeCommand, LearnedChangeView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new DismissLearnedChangeCommand(id, owner.Value), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>The fixed order: the row is read to learn whether the act loosens; a loosening asks for a fresh password for THIS act (the handler refuses without the proof as well), then the handler decides.</summary>
    private static async Task<Results<Ok<LearnedChangeView>, ProblemHttpResult>> DecideAsync(
        long id,
        LearnedPasswordRequest? request,
        HttpContext http,
        IQueryHandler<GetLearnedChangeQuery, LearnedChangeView?> read,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        Func<LearnedChangeView, bool> needsPassword,
        Func<string, bool, ValueTask<Result<LearnedChangeView>>> run,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var row = await read.HandleAsync(new GetLearnedChangeQuery(id), cancellationToken);
        if (row is null)
        {
            return TypedResults.Problem(ProblemResults.Create(StatusCodes.Status404NotFound, "No such learned change.", "learned.not_found"));
        }

        var fresh = needsPassword(row);
        var owner = fresh
            ? await OwnerAct.FreshAsync(http, passwordCheck, request?.Password, cancellationToken)
            : OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await run(owner.Value, fresh);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
