using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Confirm;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The one-tap confirm of a <c>semi_auto</c> entry (spec P12-08b, ADR 0166): the open tickets, the owner's tap and skip, and the daily unlock. A tap writes one outcome row and sends nothing; the execution service reads it. Every write needs the
/// session and the CSRF token, the actor comes from the session. No route segment names an order, an execution or a square-off.
/// </summary>
internal sealed class ExecutionConfirmEndpoints : IEndpointModule
{
    public const string ConfirmRateLimitPolicy = "execution-confirm";

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/execution").WithTags("Execution");

        group.MapGet("/confirm-tickets", GetTicketsAsync)
            .WithName("GetConfirmTickets")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("The confirm tickets of a session (date, default today in India): state=open (default, the cards waiting for a tap) or all. Each has the exact text (show it verbatim), its hash, the client order id, the reference price, the notional, the worst case at the stop, the expiry and serverNow (the database clock: count down from it, not from the browser clock alone), whether the password is needed and why, the Soft codes not passed at issue and, once decided, the outcome. Read only");

        group.MapPost("/confirm-tickets/{id:guid}/confirm", ConfirmAsync)
            .RequireRateLimiting(ConfirmRateLimitPolicy)
            .WithName("ConfirmTicket")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner's tap on one ticket: textHash must be the hash of the stored text (409 execution.text_changed, nothing stored), before the expiry by the database clock (409 execution.ticket_expired), the account still semi_auto (execution.mode_changed), execution_enabled ON (execution.disabled), no halt or kill switch (execution.halted), ready (execution.not_ready), the day unlocked (execution.not_unlocked) and the last price within the drift limit (execution.price_moved, execution.price_unavailable; the ticket is then closed refused). The password is needed when the ticket says so (400 execution.password_required, 401 auth.invalid_credentials, 423 auth.locked; it counts toward the lockout); a reason when Soft checks did not pass (400 execution.reason_required). An optional Idempotency-Key header must equal the ticket id (400 execution.values_invalid). A repeat answers the saved decision with alreadyDecided true; after a skip, expiry or refusal 409 execution.ticket_closed. It writes one confirmed outcome and sends nothing");

        group.MapPost("/confirm-tickets/{id:guid}/skip", SkipAsync)
            .RequireRateLimiting(ConfirmRateLimitPolicy)
            .WithName("SkipConfirmTicket")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("The owner declines one ticket: a decision (counted as declined), not an error. An optional plain-text reason of at most 200 characters. A session and the CSRF token, no password. 409 execution.ticket_closed when it was decided otherwise, execution.ticket_expired after its expiry");

        group.MapGet("/day-unlocks", GetUnlocksAsync)
            .WithName("GetDayUnlocks")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("The day unlocks of a session (date, default today in India): account, when it was unlocked and until when (15:30 India time). Read only");

        group.MapPost("/day-unlocks", UnlockAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("UnlockExecutionDay")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("The owner unlocks one account for today: a fresh password for this act (401 auth.invalid_credentials; an earlier step-up window does not count) and the actor from the session. Only on a trading day (409 execution.not_trading_day), only for an account in semi_auto or auto_live (409 execution.mode_not_executing) and before 15:30 India time (409 execution.unlock_closed). Locking the session does not remove it; a second unlock answers the first");
    }

    private static async Task<Results<Ok<IReadOnlyList<ConfirmTicketView>>, ProblemHttpResult>> GetTicketsAsync(
        IQueryHandler<GetConfirmTicketsQuery, Result<IReadOnlyList<ConfirmTicketView>>> handler, HttpContext http, CancellationToken cancellationToken, string? state = null, DateOnly? date = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetConfirmTicketsQuery(state, date), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<ConfirmResultView>, ProblemHttpResult>> ConfirmAsync(
        Guid id, ConfirmTicketBody request, HttpContext http, ICommandHandler<ConfirmTicketCommand, ConfirmResultView> handler, CancellationToken cancellationToken, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (idempotencyKey is not null && (!Guid.TryParse(idempotencyKey, out var key) || key != id))
        {
            return ProblemResults.From(Error.Validation(ConfirmCodes.ValuesInvalid, "The Idempotency-Key header, when given, is the ticket id."), http);
        }

        var session = http.GetVerifiedSession();
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure || session is null)
        {
            return ProblemResults.From(owner.IsFailure ? owner.Error : AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new ConfirmTicketCommand(owner.Value, session.SessionId, http.GetClientInfo(), id, request.TextHash, request.Password, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Results<Ok<ConfirmResultView>, ProblemHttpResult>> SkipAsync(
        Guid id, SkipTicketBody request, HttpContext http, ICommandHandler<SkipTicketCommand, ConfirmResultView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new SkipTicketCommand(owner.Value, id, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }

    private static async Task<Ok<IReadOnlyList<DayUnlockView>>> GetUnlocksAsync(IQueryHandler<GetDayUnlocksQuery, IReadOnlyList<DayUnlockView>> handler, HttpContext http, CancellationToken cancellationToken, DateOnly? date = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetDayUnlocksQuery(date), cancellationToken));
    }

    private static async Task<Results<Ok<DayUnlockView>, ProblemHttpResult>> UnlockAsync(
        DayUnlockBody request, HttpContext http, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<UnlockDayCommand, DayUnlockView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new UnlockDayCommand(owner.Value, request.AccountId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }
}
