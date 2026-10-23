using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Autonomy;
using XDrishti.Domain.Checklist;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The autonomy switches (spec P7-12, ADR 0091): read them, and change them as the owner. The change needs a session, the CSRF token and a recent password re-entry (step-up); the actor is built from the signed-in identity, never from the body.
/// No setting lets anything place an order; there is no order in P7.
/// </summary>
internal sealed class AutonomyEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/autonomy").WithTags("Autonomy");

        group.MapGet("/settings", GetAsync)
            .WithName("GetAutonomySettings")
            .WithSummary("The autonomy settings in force (automatic approval and reserve promotion, both OFF = manual approval by default) with the history of changes. No order is placed in any setting");

        group.MapPut("/settings", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeAutonomySettings")
            .WithSummary("Change the autonomy settings as the owner; appends an audited row. Needs a session, the CSRF token and the password in the body, checked on EVERY change (an earlier step-up window does not count and is not used up). 401 auth.invalid_credentials, 423 auth.locked, 403 autonomy.owner_only, 400 autonomy.values_invalid or autonomy.reason_invalid, 409 autonomy.stale, 429 auth.rate_limited")
            .ProducesRejections(conflict: true, notFound: false);
    }

    private static async Task<Ok<AutonomySettingsView>> GetAsync(IQueryHandler<GetAutonomySettingsQuery, AutonomySettingsView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetAutonomySettingsQuery(), cancellationToken));

    private static async Task<Results<Ok<AutonomySettingsView>, ProblemHttpResult>> ChangeAsync(
        ChangeAutonomyRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeAutonomySettingsCommand, AutonomySettingsView> handler,
        CancellationToken cancellationToken)
    {
        // A fresh password for this change: verified here with the sign-in lockout rules and audit, without opening or using the 5-minute step-up window.
        var session = http.GetVerifiedSession();
        if (session is null)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var confirmed = await passwordCheck.HandleAsync(new StepUpCommand(session.SessionId, request.Password, http.GetClientInfo(), Elevate: false), cancellationToken);
        if (confirmed.IsFailure)
        {
            return ProblemResults.From(confirmed.Error, http);
        }

        // The owner is the signed-in session's e-mail address; with no verified session there is no owner and the handler refuses.
        var email = session.Info.User.Email;
        var actor = string.IsNullOrWhiteSpace(email) ? string.Empty : OwnerConfirmation.OwnerPrefix + email;
        var result = await handler.HandleAsync(new ChangeAutonomySettingsCommand(actor, request.Reason, request.AutoApproveTickets, request.PromoteFromReserve, request.MinPWin, request.MaxAutoPerDay), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
