using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Selection;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner's switch for the event-risk filter (spec P15-06, ADR 0182). Reading is open to the session; every change needs the session, the CSRF token and a fresh password for THIS act, ON and OFF alike, and the actor comes from the session. The filter never places,
/// sizes, moves or cancels an order: with the switch ON a plan item is excluded or held, nothing more.
/// </summary>
internal sealed class EventFilterEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/selection").WithTags("Selection");

        group.MapGet("/event-filter", GetAsync)
            .WithName("GetEventFilter")
            .WithSummary("The event-risk filter switch: whether it is on (the newest decision row; no row, or an unreadable history, is off), its window in sessions (resultsWithinDays, 2 by default), the approval setting (manual by default, or auto), who decided (owner or auto), the latest 50 decision rows, the reasons the filter is not acting (switch_off, no_calendar_rows, no_entry_for_instruments with a count, history_unreadable, calendar_unreadable), the calendar coverage and the newest protocol-F verdict state (unavailable until it exists). Read only");

        group.MapPut("/event-filter", PutAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeEventFilter")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner switches the event-risk filter or changes its window or approval, with a reason of 1 to 300 characters and the password on EVERY change (401 auth.invalid_credentials, 423 auth.locked). enabled is required; resultsWithinDays (0 to 10) and approval (manual or auto) left out keep the value in force. 400 selection.reason_invalid, 400 selection.invalid_setting, 403 selection.owner_only, 409 selection.stale when the database refused the row. Setting approval to auto is itself an owner act; the automatic path may turn the filter on only from a stored KEEP verdict and is recorded as auto, never as an owner decision");
    }

    private static async Task<Ok<EventFilterView>> GetAsync(IQueryHandler<GetEventFilterQuery, EventFilterView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetEventFilterQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<EventFilterView>, ProblemHttpResult>> PutAsync(
        EventFilterChangeBody request, HttpContext http, ICommandHandler<ChangeEventFilterCommand, EventFilterView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var session = http.GetVerifiedSession();
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure || session is null)
        {
            return ProblemResults.From(owner.IsFailure ? owner.Error : AuthErrors.Unauthenticated, http);
        }

        // The handler checks the fields and the reason first and then the fresh password for every change; the actor is the session's owner.
        var result = await handler.HandleAsync(
            new ChangeEventFilterCommand(owner.Value, session.SessionId, http.GetClientInfo(), request.Enabled, request.ResultsWithinDays, request.Approval, request.Reason, request.Password), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }
}
