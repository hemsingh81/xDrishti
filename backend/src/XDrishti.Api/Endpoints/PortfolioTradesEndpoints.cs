using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Trades from a broker tradebook (CSV) or typed in by hand, and the rebuild of a holding's lots from them (ADR 0023, P2-03): the template, the preview and import of a file, the import history with Undo, the trades of a holding, adding and voiding a trade,
/// and the rebuild of one holding's lots (with a dry run). All routes need a session; every POST also needs the CSRF token; none needs a step-up (trades hold no secrets, an import is undone by voiding and a rebuild is previewed first).
/// </summary>
internal sealed class PortfolioTradesEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var trades = api.MapGroup("/portfolio/trades").WithTags("Trades");

        trades.MapGet("/template", GetTemplateAsync)
            .WithName("GetTradeTemplate")
            .WithSummary("The template of a tradebook file as a CSV download: the canonical header (Date, Symbol, ISIN, Side, Quantity, Price, Exchange, Trade ID, Time, Charges) and two example rows. Other header names are accepted too (any case, punctuation ignored); only date, symbol or ISIN, side, quantity and price are required")
            .Produces<string>(StatusCodes.Status200OK, "text/csv");

        trades.MapPost("/preview", PreviewAsync)
            .WithName("PreviewTradeImport")
            .WithSummary("Read a tradebook against an account and say what importing it would do, storing and changing nothing. Body: multipart/form-data with accountId (an account with the Portfolio role) and file (CSV, UTF-8, at most 10 MB and 20,000 data rows). Returns the file (rows, parsed, skipped by reason, bad rows with line and reason - at most 200 listed, badTotal counts all -, duplicates, alreadyImported, newTrades), the securities - every held or master-listed one and at most 200 unmatched ones; securitiesTotal counts all, unmatchedListed the unmatched listed - (symbol, isin, matched, holdingId, trades, buys, sells, first and last trade date and the rebuild: status ok | unchanged | quantity_mismatch | no_holding | ambiguous, reason, lotsBefore, lotsAfter, handDatedLotsReplaced, syncLotsReplaced, realisedPnl, adjustedFor, q, net, openingQty {before, after}, openCostAfter, brokerCost, costDelta, costNote, digest), notHeld and unmatched {securities, trades} and the overall digest. 400 portfolio.wrong_file_kind (extension detected: dividends for a dividend payout list - import it as dividends -, tradebook_without_sides for a tradebook with no buy/sell column); 400 portfolio.invalid (field file: unreadable, too big, missing columns - with the accepted names -, not UTF-8; accountId); 404 portfolio.not_found (account); 409 portfolio.role_off")
            .Accepts<TradeUploadForm>("multipart/form-data")
            .ProducesRejections(conflict: true);

        trades.MapPost("/import", ImportAsync)
            .WithName("ImportTrades")
            .WithSummary("Import a tradebook: body multipart/form-data with accountId, file, rebuild (true | false, default true) and digest (required with rebuild: the digest of the preview). Stores the trades the account does not have yet as one batch (a trade id of the file together with the trade date - exchanges number trades per day -, else a hash of the line, is the identity: the same file again stores nothing and makes no batch) and, with rebuild, replaces the lots of every holding whose result is ok by lots built from the real buy and sell dates. Trades of securities not held, of mismatching or ambiguous holdings are stored and not applied. 409 portfolio.preview_stale when holdings, lots, corporate actions or trades changed since the preview (extension report: the fresh preview; nothing applied); 409 portfolio.sync_running while the account's holdings sync runs; 400 portfolio.wrong_file_kind (extension detected: dividends | tradebook_without_sides, as for the preview); 400 portfolio.invalid; 404 portfolio.not_found. Audited with counts only")
            .Accepts<TradeUploadForm>("multipart/form-data")
            .ProducesRejections(conflict: true);

        trades.MapPost("/", AddAsync)
            .WithName("AddTrade")
            .WithSummary("Add one trade by hand: holdingId, or accountId with symbol and/or isin of an NSE share or ETF of the instrument master; side buy | sell, tradeDate (1990-01-02 to today), tradeTime (optional, India time), quantity (whole, above 0), price (above 0), charges (optional). Validated like a CSV row; identical manual trades are not deduplicated. Changes no lots - rebuild them afterwards. 400 portfolio.invalid with field errors; 404 portfolio.not_found. Audited")
            .ProducesRejections(conflict: true);

        trades.MapPost("/{id:guid}/void", VoidAsync)
            .WithName("VoidTrade")
            .WithSummary("Void a trade: it stops counting, is never deleted and can be imported again. Voiding a voided trade is a no-op. Changes no lots - rebuild them afterwards. 404 portfolio.not_found. Audited")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var batches = api.MapGroup("/portfolio/trade-batches").WithTags("Trades");

        batches.MapGet("/", ListBatchesAsync)
            .WithName("ListTradeBatches")
            .WithSummary("The imports of tradebook files, newest first: accountId (optional), page, pageSize (max 100, default 50). Each has id, accountId, accountName, fileName, sha256, parsed, imported, duplicates, skipped, bad, status imported | undone, activeTrades, securities, createdBy, createdAt, undoneAt")
            .ProducesValidationProblem();

        batches.MapPost("/{id:guid}/undo", UndoAsync)
            .WithName("UndoTradeBatch")
            .WithSummary("Undo an import: voids its trades (nothing is deleted) and rebuilds from the remaining trades the lots of the affected holdings whose lots came from trades - with none left, one lot of the broker's quantity and cost dated by the first import date, flagged default. LIMITATION: an undo cannot bring back lots the owner dated by hand or a sync added that an earlier rebuild replaced; handDatedLotsReplaced and syncLotsReplaced say how many the rebuild removes (the lots of holdings it rebuilds). Holdings whose lots did not come from trades keep their lots; holdings that cannot be rebuilt (quantity mismatch, ambiguous) keep theirs and are listed in notRebuilt [{symbol, holdingId, reason}] (holdingsNotRebuilt counts them). dryRun=true (default false) changes nothing and returns the same result (dryRun, applied=false) so the confirmation can say what will happen. Undoing twice is a no-op (alreadyUndone). 409 portfolio.sync_running (not for a dry run); 404 portfolio.not_found. Audited with counts only")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var holdings = api.MapGroup("/holdings/{id:guid}").WithTags("Trades");

        holdings.MapGet("/trades", ListHoldingTradesAsync)
            .WithName("ListHoldingTrades")
            .WithSummary("The trades of a holding's security (by ISIN, else symbol), newest first, voided ones included and flagged (voided, voidedAt, voidedBy): page, pageSize (max 200, default 50)")
            .ProducesRejections(conflict: false);

        holdings.MapPost("/rebuild-lots", RebuildLotsAsync)
            .WithName("RebuildHoldingLots")
            .WithSummary("Rebuild a holding's lots from its stored trades. dryRun (default true) returns the rebuild block - status, counts of lots replaced, realised profit, adjustments, the broker's quantity against the trades - and changes nothing; dryRun=false replaces the lots when the status is ok (applied) and leaves them for unchanged, quantity_mismatch and ambiguous. A holding without trades is rebuilt into one default-dated lot. 409 portfolio.sync_running (not for a dry run); 404 portfolio.not_found. Audited with counts only")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<ContentHttpResult> GetTemplateAsync(HttpContext http, IQueryHandler<GetTradeTemplateQuery, string> handler, CancellationToken cancellationToken)
    {
        var csv = await handler.HandleAsync(new GetTradeTemplateQuery(), cancellationToken);
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{TradeTemplate.FileName}\"";
        return TypedResults.Text(csv, "text/csv", Encoding.UTF8);
    }

    private static async Task<Results<Ok<TradePreviewReport>, ProblemHttpResult>> PreviewAsync(
        HttpContext http,
        IQueryHandler<PreviewTradesQuery, Result<TradePreviewReport>> handler,
        CancellationToken cancellationToken)
    {
        var upload = await TradeUploads.ReadAsync(http, forImport: false, cancellationToken);
        return upload.IsFailure
            ? ProblemResults.From(upload.Error, http)
            : Respond(await handler.HandleAsync(new PreviewTradesQuery(upload.Value.AccountId, upload.Value.FileName, upload.Value.Content), cancellationToken), http);
    }

    private static async Task<Results<Ok<TradeImportResult>, ProblemHttpResult>> ImportAsync(
        HttpContext http,
        ICommandHandler<ImportTradesCommand, TradeImportResult> handler,
        CancellationToken cancellationToken)
    {
        var upload = await TradeUploads.ReadAsync(http, forImport: true, cancellationToken);
        return upload.IsFailure
            ? ProblemResults.From(upload.Error, http)
            : Respond(
                await handler.HandleAsync(
                    new ImportTradesCommand(upload.Value.AccountId, upload.Value.FileName, upload.Value.Content, upload.Value.Rebuild, upload.Value.Digest, http.GetActor()),
                    cancellationToken),
                http);
    }

    private static async Task<Results<Ok<TradeView>, ProblemHttpResult>> AddAsync(
        ManualTradeInput input,
        HttpContext http,
        ICommandHandler<AddManualTradeCommand, TradeView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new AddManualTradeCommand(input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<TradeView>, ProblemHttpResult>> VoidAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<VoidTradeCommand, TradeView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new VoidTradeCommand(id, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<TradeBatchPage>, ProblemHttpResult>> ListBatchesAsync(
        HttpContext http,
        IQueryHandler<ListTradeBatchesQuery, Result<TradeBatchPage>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListTradeBatchesQuery(accountId, page, pageSize), cancellationToken), http);

    private static async Task<Results<Ok<TradeUndoResult>, ProblemHttpResult>> UndoAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<UndoTradeBatchCommand, TradeUndoResult> handler,
        CancellationToken cancellationToken,
        bool dryRun = false) =>
        Respond(await handler.HandleAsync(new UndoTradeBatchCommand(id, http.GetActor(), dryRun), cancellationToken), http);

    private static async Task<Results<Ok<TradePage>, ProblemHttpResult>> ListHoldingTradesAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<ListHoldingTradesQuery, Result<TradePage>> handler,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListHoldingTradesQuery(id, page, pageSize), cancellationToken), http);

    private static async Task<Results<Ok<HoldingRebuildResult>, ProblemHttpResult>> RebuildLotsAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<RebuildLotsCommand, HoldingRebuildResult> handler,
        CancellationToken cancellationToken,
        bool dryRun = true) =>
        Respond(await handler.HandleAsync(new RebuildLotsCommand(id, dryRun, http.GetActor()), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}
