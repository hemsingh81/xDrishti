using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Groups;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Holdings of the Portfolio accounts (ADR 0012): the list with returns, the summary, the lots and their dates, and the account's preview, sync and
/// refresh status (the first import is part of saving the Portfolio settings, ADR 0013). Reading and syncing public portfolio data needs a session but no step-up. Percentages are percent numbers (16.1 = 16.1 %).
/// </summary>
internal sealed class HoldingsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var holdings = api.MapGroup("/holdings").WithTags("Holdings");

        holdings.MapGet("/", ListAsync)
            .WithName("ListHoldings")
            .WithSummary("Holdings with lots summary, profit/loss, XIRR and day change: accountId, bucketId, status (open default, closed, all), q (symbol, name, ISIN), sort (symbol, name, qty, avgCost, lastPrice, invested, value, pnl, pnlPct, xirr, dayChange, dayChangePct, acquiredOn, bucket, account; prefix - for descending; rows without a value last), page, pageSize (max 100), groupBy (account, bucket, sector, basket) with groupKey (account id, bucket id, sector name, basket id, or ~none for Unclassified / No basket) to list one group's holdings; an unknown key gives an empty page. With groupBy and no accountId only accounts that count in totals are listed (includeInTotals), like the groups and the summary, while the list without groupBy includes the others too. Each row has previousClose, dayChange (rupees), dayChangePct (percent), priceAt and priceSource (sync, quote or live)")
            .ProducesValidationProblem();

        holdings.MapGet("/summary", GetSummaryAsync)
            .WithName("GetHoldingsSummary")
            .WithSummary("Totals of the holdings of an account or bucket (all accounts that count in totals when neither is given): invested, value, P&L, XIRR, day change (dayChange, dayChangePct, dayChangeExcluded: open holdings left out for lack of a previous close), counts, lots on the default date, pricesAsOf (oldest and newest price time), last sync")
            .ProducesProblem(StatusCodes.Status404NotFound);

        holdings.MapGet("/groups", GetGroupsAsync)
            .WithName("GetHoldingGroups")
            .WithSummary("The holdings grouped by account, bucket, sector or basket (by): per group holdings, unpriced, invested, value, P&L (rupees and percent), realised, XIRR (from the union of the group's cash flows) and xirrState, day change, dayChangeExcluded and weightPct (share of the scope's total value), plus the scope total; additive is false for baskets (a holding is in every live basket that holds it, or in No basket; groups overlap). Filters: accountId, bucketId, status (open default, closed, all), q; sort (value, invested, pnl, pnlPct, xirr, dayChange, name; prefix - for descending; default largest value first; groups without a value last; at most 200 groups). stackBy (account when by=sector, sector when by=account) adds parts[{key, name, value}] to each group. Group keys: account id, bucket id, sector name, basket id, ~none for Unclassified / No basket (a master sector spelled ~none counts as Unclassified). Without accountId only accounts that count in totals (includeInTotals) are in the groups, the total and the children, unlike the flat holdings list which includes the others; an unknown accountId gives 200 with no groups (not 404 as for the summary); more than 200 groups are cut off without a flag, the total still covers the whole scope. A group of only unpriced holdings, and a total of only unpriced holdings, have null value, pnl, pnlPct, xirr and xirrState no_price (the summary shows value 0)")
            .ProducesValidationProblem();

        holdings.MapGet("/{id:guid}/lots", GetLotsAsync)
            .WithName("GetHoldingLots")
            .WithSummary("The lots of a holding with the profit/loss and XIRR of each")
            .ProducesProblem(StatusCodes.Status404NotFound);

        holdings.MapPut("/{id:guid}/lots/{lotId:guid}", UpdateLotDateAsync)
            .WithName("UpdateHoldingLotDate")
            .WithSummary("Set the acquisition date of a lot (1990-01-01 to today); the lot stops using the default date. A lot rebuilt from trades (source trade) takes its date from the trade: 409 portfolio.lot_from_trade; the opening lot of such a holding must stay before the earliest trade: 400 portfolio.invalid, field acquiredOn")
            .ProducesRejections(conflict: true);

        var account = api.MapGroup("/accounts/{id:guid}/holdings").WithTags("Holdings");

        account.MapPost("/preview", PreviewAsync)
            .WithName("PreviewAccountHoldings")
            .WithSummary("Fetch the account's holdings from Dhan for the first import in the portfolio settings, saving nothing; defaultDate (and each row's suggestedDate) is the account's first import date, 2025-01-01 until the owner chooses another")
            .ProducesBrokerRejections();

        account.MapPost("/sync", SyncAsync)
            .WithName("SyncAccountHoldings")
            .WithSummary("Sync now: bring the holdings in line with Dhan (an account without holdings is imported instead, every holding dated with its first import date and the run recorded as an import; new holdings dated today, quantity changes as lots, sales closed first-in first-out); optional body {confirm, expectedOpenHoldings, expectedWouldClose}; 409 portfolio.sync_running while a sync runs; 409 portfolio.confirmation_required (listedHoldings, openHoldings, wouldClose) when Dhan's answer would close all or most holdings, unless confirm is true with the numbers echoed back; 409 portfolio.confirmation_stale when the answer differs from the confirmed numbers")
            .ProducesBrokerRejections()
            .ProducesValidationProblem();

        account.MapGet("/sync-status", GetSyncStatusAsync)
            .WithName("GetAccountHoldingsSyncStatus")
            .WithSummary("The account's refresh settings (mode, daily time, first import date, hasHoldings), the next scheduled run and the latest sync run")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<HoldingPage>, ProblemHttpResult>> ListAsync(
        HttpContext http,
        IQueryHandler<ListHoldingsQuery, Result<HoldingPage>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        Guid? bucketId = null,
        string? status = null,
        [FromQuery(Name = "q")] string? q = null,
        string? sort = null,
        int? page = null,
        int? pageSize = null,
        string? groupBy = null,
        string? groupKey = null) =>
        Respond(await handler.HandleAsync(new ListHoldingsQuery(new HoldingsRequest(accountId, bucketId, status, q, sort, page, pageSize, groupBy, groupKey)), cancellationToken), http);

    private static async Task<Results<Ok<HoldingGroups>, ProblemHttpResult>> GetGroupsAsync(
        HttpContext http,
        IQueryHandler<GetHoldingGroupsQuery, Result<HoldingGroups>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        Guid? bucketId = null,
        string? status = null,
        [FromQuery(Name = "q")] string? q = null,
        string? by = null,
        string? sort = null,
        string? stackBy = null) =>
        Respond(await handler.HandleAsync(new GetHoldingGroupsQuery(new HoldingGroupsRequest(accountId, bucketId, status, q, by, sort, stackBy)), cancellationToken), http);

    private static async Task<Results<Ok<HoldingsSummary>, ProblemHttpResult>> GetSummaryAsync(
        HttpContext http,
        IQueryHandler<GetHoldingsSummaryQuery, Result<HoldingsSummary>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        Guid? bucketId = null) =>
        Respond(await handler.HandleAsync(new GetHoldingsSummaryQuery(accountId, bucketId), cancellationToken), http);

    private static async Task<Results<Ok<HoldingLots>, ProblemHttpResult>> GetLotsAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetHoldingLotsQuery, Result<HoldingLots>> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new GetHoldingLotsQuery(id), cancellationToken), http);

    private static async Task<Results<Ok<LotRow>, ProblemHttpResult>> UpdateLotDateAsync(
        Guid id,
        Guid lotId,
        LotDateInput input,
        HttpContext http,
        ICommandHandler<UpdateLotDateCommand, LotRow> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UpdateLotDateCommand(id, lotId, input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<ImportPreview>, ProblemHttpResult>> PreviewAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<PreviewHoldingsQuery, Result<ImportPreview>> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new PreviewHoldingsQuery(id), cancellationToken), http);

    private static async Task<Results<Ok<SyncRunView>, ProblemHttpResult>> SyncAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<SyncHoldingsCommand, SyncRunView> handler,
        CancellationToken cancellationToken,
        [FromBody] SyncInput? input = null) =>
        Respond(await handler.HandleAsync(new SyncHoldingsCommand(id, SyncTrigger.Manual, http.GetActor(), input?.Confirm ?? false, input?.ExpectedOpenHoldings, input?.ExpectedWouldClose), cancellationToken), http);

    private static async Task<Results<Ok<SyncStatusView>, ProblemHttpResult>> GetSyncStatusAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetSyncStatusQuery, Result<SyncStatusView>> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new GetSyncStatusQuery(id), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}
