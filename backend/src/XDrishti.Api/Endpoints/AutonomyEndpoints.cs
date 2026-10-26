using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Autonomy;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The autonomy switches (spec P7-12, ADR 0091, P8-19, ADR 0130): read them, and change them as the owner. The change needs a session, the CSRF token, the capability autonomy_form and a fresh password for this change; the actor is built from the signed-in identity, never from the body.
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
            .WithSummary("Change the autonomy settings as the owner; appends an audited row. Needs a session, the CSRF token, the capability autonomy_form (off by default) and the password in the body, checked on EVERY change (an earlier step-up window does not count and is not used up); optional expectedSettingsId (the row the form was loaded at, 0 for none) makes a stale form a 409. 403 capabilities.off, 401 auth.invalid_credentials, 423 auth.locked, 403 autonomy.owner_only, 400 autonomy.values_invalid or autonomy.reason_invalid, 409 autonomy.stale, 429 auth.rate_limited")
            .ProducesRejections(conflict: true, notFound: false);
    }

    private static async Task<Ok<AutonomySettingsView>> GetAsync(IQueryHandler<GetAutonomySettingsQuery, AutonomySettingsView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetAutonomySettingsQuery(), cancellationToken));

    private static Task<Results<Ok<AutonomySettingsView>, ProblemHttpResult>> ChangeAsync(
        ChangeAutonomyRequest request,
        HttpContext http,
        ICapabilityGate gate,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        IOwnerActRunner runner,
        ICommandHandler<ChangeAutonomySettingsCommand, AutonomySettingsView> handler,
        CancellationToken cancellationToken) =>
        // The capability autonomy_form first, then a fresh password for this change, then the owner from the session (spec P8-19, ADR 0130). There is no other path: nothing writes these switches except this call and the owner's command line.
        OwnerActFlow.RunAsync(
            http, gate, passwordCheck, runner, handler, Domain.Capabilities.Capabilities.AutonomyForm, request.Password,
            actor => Result.Success(new ChangeAutonomySettingsCommand(actor, request.Reason, request.AutoApproveTickets, request.PromoteFromReserve, request.MinPWin, request.MaxAutoPerDay, request.ExpectedSettingsId)),
            AccountEventType.AutonomySettingsChanged,
            view => string.Create(CultureInfo.InvariantCulture, $"settings={view.Id} auto_approve={(view.AutoApproveTickets ? "on" : "off")} reserve={(view.PromoteFromReserve ? "on" : "off")}"),
            cancellationToken);
}
