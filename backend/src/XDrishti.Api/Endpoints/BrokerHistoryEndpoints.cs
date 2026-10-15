using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The trades and the ledger of a Portfolio account read from Dhan (ADR 0025, P2-13): "Sync now" (which starts the run and answers at once - a first backfill takes minutes), the status of the sync and the list of the ledger entries. All routes need a
/// session; the POST also needs the CSRF token; none needs a step-up (the data is the owner's own account data and nothing is sent to Dhan but a read).
/// </summary>
internal sealed class BrokerHistoryEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var account = api.MapGroup("/accounts/{id:guid}").WithTags("Broker history");

        account.MapPost("/sync/broker-history", StartAsync)
            .WithName("SyncAccountBrokerHistory")
            .WithSummary("Sync now: read the account's trades and then its ledger from Dhan (needs the Portfolio role and a connected Dhan token). Optional body {from} (an India date from 1990-01-02 to today): the first date to read instead of the watermark - for an account never read, its first import date. An earlier date than the watermark reads dates again (nothing is stored twice); a date later than the day after the watermark of either kind is refused, because the dates in between would never be read (when only one kind was read so far, also a date later than the account's first import date, because the other kind would then begin at the chosen date). The dates are read in windows of at most 30 days; new trades are stored next to the imported ones (source broker) and the lots of the holdings are rebuilt from the real dates where that is safe. Answers at once with the trades run as it begins (status running); poll the status. 409 portfolio.sync_running while a holdings sync or another trades or ledger run of the account runs; 409 portfolio.not_connected or portfolio.token_expired without a usable Dhan token; 400 portfolio.invalid (field from: not a plausible date, or leaves a hole after the watermark); 404 accounts.not_found. Audited with counts only")
            .ProducesRejections(conflict: true);

        account.MapGet("/sync/broker-history/status", GetStatusAsync)
            .WithName("GetAccountBrokerHistoryStatus")
            .WithSummary("The status of the trades and ledger sync: running, for each kind the watermark (last date read), the latest run (status, dates, counts, error) and windowsDone / windowsTotal, the next scheduled run (null when the service is paused) and, from the latest SUCCEEDED trades run and no other (nothing is carried forward from older runs; a skipped, failed or running run neither changes nor erases them): holdingsRebuilt, holdingsNeedingReview (a hand-dated lot; use the holding's rebuild preview) and needsReviewSymbols (their trading symbols, at most 50; holdingsNeedingReview says how many in all). Health (ADR 0027): each kind also has attempts (the number of the latest run within the day's occurrence: 1 for the first, up to 4 with the three quiet retries), consecutiveFailedDays (days in a row whose last attempt failed; 0 when the latest succeeded), lastFailureReason (unavailable, rate_limited, interrupted, rejected, history_too_large, token_expired, token_refused, not_connected, unexpected; null while healthy) and retryAt (when the next quiet retry is due, null when none is pending); the run counts carry attempts, failureReason and deepenedTo. deepenedTo is the earliest date the nightly deepening of the trade history reached (one earlier year a night while the trades do not explain a holding in a way earlier history could fix; null: none read) and deepenExhausted that it reads no more (Dhan's history ends there or the furthest year back is reached). Dropped rows (roadmap ISS-03): each kind also has droppedByReason (how many rows the latest run of that kind dropped per reason code; a later clean or skipped run shows none: unreadable_row, missing_id, bad_id, missing_side, bad_quantity, bad_price, bad_time for trades; bad_date, missing_amount, bad_amount, zero_amount for the ledger; failed_checks, store_refused, other) and droppedRows (from the same latest run: the place of the first five: windowFrom, windowTo, page, rowIndex, reason - never a row's content); the same two fields are in the run counts (lastRun.counts), where droppedByReason adds up to dropped. Old runs have both empty")
            .ProducesProblem(StatusCodes.Status404NotFound);

        account.MapGet("/ledger", ListLedgerAsync)
            .WithName("ListAccountLedger")
            .WithSummary("The account's ledger entries read from Dhan, newest first: from, to (India dates, inclusive), class (funds_in, funds_out, dividend, charges, other), page, pageSize (max 200, default 50). Each entry has date, voucherNo, description, narration (Dhan's free text - show as plain text), exchange, amount (signed: credit minus debit), class and runningBalance. totals lists the count and signed amount of every class for the period from to to, whatever class is filtered. The ledger's entries of class charges count as costs in the net returns of an account and the portfolio, and an entry of class dividend is offered as a suggested dividend (see the dividends endpoints); nothing else of the ledger is part of returns")
            .ProducesRejections(conflict: false);
    }

    private static async Task<Results<Ok<BrokerHistoryRunView>, ProblemHttpResult>> StartAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<StartBrokerHistoryCommand, BrokerHistoryRunView> handler,
        CancellationToken cancellationToken,
        [FromBody] BrokerHistoryInput? input = null) =>
        Respond(await handler.HandleAsync(new StartBrokerHistoryCommand(id, input?.From, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<BrokerHistoryStatusView>, ProblemHttpResult>> GetStatusAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetBrokerHistoryStatusQuery, Result<BrokerHistoryStatusView>> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new GetBrokerHistoryStatusQuery(id), cancellationToken), http);

    private static async Task<Results<Ok<LedgerPage>, ProblemHttpResult>> ListLedgerAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<ListLedgerQuery, Result<LedgerPage>> handler,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        [FromQuery(Name = "class")] string? entryClass = null,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListLedgerQuery(id, from, to, entryClass, page, pageSize), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}
