using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Planning;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Price-crossing alerts and post-trade prompts on the plan screen (spec P7-20, ADR 0115). The read shows the stored crossings, prompts and acts of a plan; the three writes (mute, snooze, dismiss) are notification preferences of the owner: they need a session
/// and the CSRF token, and the actor is built here from the signed-in e-mail address (<c>owner:</c> + address), never from the body. They change no ticket and no order exists. The bell has no button: it reads <c>GET /api/plans/notices</c>.
/// </summary>
internal sealed class TicketAlertsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/plans").WithTags("Ticket alerts");

        group.MapGet("/{id:long}/alerts", AlertsAsync)
            .WithName("GetPlanAlerts")
            .WithSummary("The price crossings of the plan's watched tickets (stop or target level reached, once each), the post-trade prompts with their derived status (open, matched with via journal or trade_on_record, expired_no_fill, ambiguous) and which tickets the owner muted. 'enabled' is false while Planning:TicketAlerts:Enabled is off and the lists are then empty. Alerts only: no order is placed and none exists. Read only. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/items/{itemId:long}/alerts/mute", MuteAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("MutePlanItemAlerts")
            .WithSummary("Stop further crossing alerts of one ticket for its session (an insert-only owner act; earlier crossings stay visible; the ticket does not change). Session and CSRF token; the owner is the session. 409 plans.alert_closed after the session, 404 plans.not_found")
            .ProducesRejections(conflict: true);

        group.MapPost("/alerts/{noticeId}/snooze", SnoozeAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("SnoozePlanAlert")
            .WithSummary("Hide a crossing or a prompt (noticeId crossing:{id} or prompt:{id}) for 15, 30 or 60 minutes; it reappears once, and can be snoozed three times. Session and CSRF token; insert-only. 422 plans.invalid, 409 plans.snooze_limit or plans.alert_closed (dismissed), 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true);

        group.MapPost("/alerts/{noticeId}/dismiss", DismissAsync)
            .RequireRateLimiting(SessionAuthentication.PlanDecisionRateLimitPolicy)
            .WithName("DismissPlanAlert")
            .WithSummary("Hide a crossing or a prompt for good (noticeId crossing:{id} or prompt:{id}). Session and CSRF token; insert-only; repeating it changes nothing. 422 plans.invalid, 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: false);
    }

    // The owner is the signed-in session's e-mail address; with no verified session there is no owner (401). Nothing from the request body or query is read.
    private static string? OwnerActor(HttpContext http) =>
        http.GetVerifiedSession()?.Info.User.Email is { } email && !string.IsNullOrWhiteSpace(email) ? OwnerConfirmation.OwnerPrefix + email : null;

    private static async Task<Results<Ok<PlanAlertsView>, ProblemHttpResult>> AlertsAsync(long id, HttpContext http, IQueryHandler<GetPlanAlertsQuery, Result<PlanAlertsView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPlanAlertsQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> MuteAsync(long itemId, HttpContext http, ICommandHandler<MuteTicketAlertsCommand> handler, CancellationToken cancellationToken)
    {
        if (OwnerActor(http) is not { } actor)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new MuteTicketAlertsCommand(itemId, actor), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SnoozeAsync(string noticeId, SnoozeAlertRequest? request, HttpContext http, ICommandHandler<SnoozeTicketNoticeCommand> handler, CancellationToken cancellationToken)
    {
        if (OwnerActor(http) is not { } actor)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new SnoozeTicketNoticeCommand(noticeId, request?.Minutes ?? 0, actor), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DismissAsync(string noticeId, HttpContext http, ICommandHandler<DismissTicketNoticeCommand> handler, CancellationToken cancellationToken)
    {
        if (OwnerActor(http) is not { } actor)
        {
            return ProblemResults.From(AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new DismissTicketNoticeCommand(noticeId, actor), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.NoContent();
    }

    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == PlanErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}

/// <summary>The body of a snooze: 15, 30 or 60.</summary>
internal sealed record SnoozeAlertRequest(int Minutes);
