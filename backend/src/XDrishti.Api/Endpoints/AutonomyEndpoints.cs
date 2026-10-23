using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
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
            .RequireStepUp()
            .WithName("ChangeAutonomySettings")
            .WithSummary("Change the autonomy settings as the owner; appends an audited row. Needs step-up. 403 autonomy.owner_only, 400 autonomy.values_invalid or autonomy.reason_invalid, 409 autonomy.stale")
            .ProducesRejections(conflict: true, notFound: false);
    }

    private static async Task<Ok<AutonomySettingsView>> GetAsync(IQueryHandler<GetAutonomySettingsQuery, AutonomySettingsView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetAutonomySettingsQuery(), cancellationToken));

    private static async Task<Results<Ok<AutonomySettingsView>, ProblemHttpResult>> ChangeAsync(
        ChangeAutonomyRequest request,
        HttpContext http,
        ICommandHandler<ChangeAutonomySettingsCommand, AutonomySettingsView> handler,
        CancellationToken cancellationToken)
    {
        // The owner is the signed-in session's e-mail address; with no verified session there is no owner and the handler refuses.
        var email = http.GetVerifiedSession()?.Info.User.Email;
        var actor = string.IsNullOrWhiteSpace(email) ? string.Empty : OwnerConfirmation.OwnerPrefix + email;
        var result = await handler.HandleAsync(new ChangeAutonomySettingsCommand(actor, request.Reason, request.AutoApproveTickets, request.PromoteFromReserve, request.MinPWin, request.MaxAutoPerDay), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
