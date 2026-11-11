using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Planning;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The loosening banner of the plan screen (spec BK-10b, ADR 0265). The banner itself is part of the plan read (<c>loosenings</c> of <c>GET /api/plans/{id}</c>); here the owner hides one loosening (a session and the CSRF token, no password as the other dismissals of the plan
/// screen, the actor from the session, insert-only) and reads or changes the automatic dismissal setting (OFF by default; a fresh password on every change). Nothing here changes a limit, a stored plan or its hash, and nothing places an order.
/// </summary>
internal sealed class LooseningEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/plans").WithTags("Plans");

        group.MapPost("/loosenings/{noticeId}/dismiss", DismissAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("DismissLoosening")
            .WithSummary("Hide one loosened limit (noticeId sizing:{id}, budget:{id} or basket:{id}) on every plan read from now on. Session and CSRF token, no password; insert-only, the owner is the session; repeating it changes nothing (204). The change itself is never edited. 422 plans.invalid (a malformed id or a change that did not loosen), 404 plans.not_found, 401")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: false);

        group.MapGet("/loosening-settings", GetSettingsAsync)
            .WithName("GetLooseningSettings")
            .WithSummary("The automatic dismissal setting plans.loosening_banner.auto_dismiss (OFF by default; no row means OFF) with the version and the latest 50 changes. When ON, a loosening a published plan already showed is hidden at the build of the next plan, recorded as automatic. Read only");

        group.MapPut("/loosening-settings", ChangeSettingsAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeLooseningSettings")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner switches the automatic dismissal ON or OFF (autoDismiss, expectedVersion = the version read). The password is needed on EVERY change, ON and OFF alike (403 auth.step_up_required without one, 401 auth.invalid_credentials, 423 auth.locked); a row is appended with the owner as actor. 409 plans.stale when the setting changed meanwhile");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DismissAsync(string noticeId, HttpContext http, ICommandHandler<DismissLooseningCommand> handler, CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new DismissLooseningCommand(noticeId, owner.Value), cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : result.Error.Code == PlanErrors.InvalidCode
                ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Message, result.Error.Code))
                : ProblemResults.From(result.Error, http);
    }

    private static async Task<Ok<LooseningSettingsView>> GetSettingsAsync(IQueryHandler<GetLooseningSettingsQuery, LooseningSettingsView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetLooseningSettingsQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<LooseningSettingsView>, ProblemHttpResult>> ChangeSettingsAsync(
        LooseningSettingsChangeBody request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeLooseningSettingsCommand, LooseningSettingsView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeLooseningSettingsCommand(owner.Value, request.AutoDismiss, request.ExpectedVersion), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }
}

/// <summary>The owner's change of the automatic dismissal setting. The actor is never taken from the body.</summary>
/// <param name="AutoDismiss">The new value of <c>plans.loosening_banner.auto_dismiss</c>.</param>
/// <param name="ExpectedVersion">The version read (0 when no row exists); another change in between is 409 plans.stale.</param>
/// <param name="Password">The owner's password for THIS change, ON or OFF. Missing: 403 auth.step_up_required.</param>
internal sealed record LooseningSettingsChangeBody(bool AutoDismiss, long ExpectedVersion, string? Password);
