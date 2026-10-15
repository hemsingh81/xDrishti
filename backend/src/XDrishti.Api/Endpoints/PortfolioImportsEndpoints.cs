using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Dhan's downloaded reports (ADR 0029, P2-17): one preview and one import for any number of files, each recognised by its title and header (global transaction report as bill-level trades, depository charges as account costs, demat holding statement as a check and a
/// source of names, a tradebook as in ADR 0023), and the history and Undo of the cost imports; the trade and dividend batches have their own endpoints. All routes need a session; every POST also needs the CSRF token; none needs a step-up (no secrets are involved and every
/// import is undone by voiding).
/// </summary>
internal sealed class PortfolioImportsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var imports = api.MapGroup("/portfolio/imports").WithTags("Imports");

        imports.MapPost("/preview", PreviewAsync)
            .WithName("PreviewImports")
            .WithSummary("Recognise downloaded files by their title and header and say what importing each would do, storing and changing nothing. Body: multipart/form-data with accountId (an account with the Portfolio role), files (one or more, at most 20, 10 MB each; CSV, XLS or XLSX - the format is judged from the first bytes) and optionally choices (a JSON array of {name, holdingId | instrumentId | isin, remember}: what scrip names of a global transaction report mean; name is the name as written or the item's nameKey, and the choice applies to every file). Answers one result per file, in order: fileName, kind (global_transaction_report | dp_charges | demat_holding | pnl_report | trades | dividends | tradebook_without_sides | statement | unknown), supported, routeTo (dividends: import it on the dividend import), message (why a file cannot be imported), error (code and message: unreadable file, or portfolio.wrong_file_kind for an unknown shape - the message lists the accepted kinds), summary {rows, valid, toAdd, alreadyImported, skipped, bad, from, to}, warnings [{code, message, count}] (other_account, bad_rows, needs_match, partial_name_match, charges_differ_from_footer, negative_charges, value_not_reproduced, covered_by_dhan, totals_differ, holdings_differ, notice_only), and the block of the kind: trades (the tradebook preview of ADR 0023, as for a global transaction report: lots, hand-dated lots replaced, realised profit, digest) with bill {names [{name, nameKey, symbolHint, status resolved | ambiguous | needs_match, kind, isin, symbol, holdingId, instrumentId, rows, trades, candidates}], needsMatch, coverage {covered, differences}, charges {total, reportTotal, matches}} for a global transaction report, costs {lines, counts, amountToStore, names} for depository charges, demat {asOf, securities, matching, quantityDifferences, heldNotSynced, syncedNotInStatement, names} for a demat statement - which changes no holding and raises no notice. digest fingerprints what is shown. The personal lines of a report (name, client code, mobile, e-mail) are never read into the answer. 400 portfolio.invalid (form fields); 404 portfolio.not_found (account); 409 portfolio.role_off")
            .Accepts<ReportUploadForm>("multipart/form-data")
            .ProducesRejections(conflict: true);

        imports.MapPost("/import", ImportAsync)
            .WithName("ImportReports")
            .WithSummary("Import downloaded files: body multipart/form-data with accountId, files (as for the preview), digests (a JSON array with the preview's digest, or null, for each file in order; required for depository charges and demat statements, and for trade files with rebuild), choices (as for the preview; remember saves a choice for later files), rebuild (true | false, default true: rebuild the lots of the holdings whose result is ok, like a tradebook import) and acceptOtherAccount (a JSON array of the indexes, from 0, of files to import although the report's client code (UCC) differs from that of the reports imported into the account before; the first import with a code remembers its hash - never the code - and an account that remembers none never warns). Each file is imported on its own, all or nothing, and answers: fileName, kind, status (imported | nothing_new | not_supported | rejected), message, error, preview (the fresh preview when the confirmed one went stale: error portfolio.preview_stale, nothing stored), trades (a TradeImportResult: batch, imported, alreadyImported, rebuilt, holdingsRebuilt, lotsReplaced, report - undo it with the trade batch endpoint), costs (batch, imported, amount, alreadyImported, alreadyInLedger, namesLearned - undo it with the cost batch endpoint), demat (namesLearned, namesKnown) and aliasesSaved. A global transaction report is stored as bill-level trades; a trade the broker's own history has for the same day, ISIN and side is left out, and bill-level rows are voided as superseded when the broker sync later stores such trades. 400 portfolio.invalid; 404 portfolio.not_found; 409 portfolio.role_off. Audited with counts only")
            .Accepts<ReportUploadForm>("multipart/form-data")
            .ProducesRejections(conflict: true);

        var batches = api.MapGroup("/portfolio/cost-batches").WithTags("Imports");

        batches.MapGet("/", ListBatchesAsync)
            .WithName("ListCostBatches")
            .WithSummary("The imports of depository-charges sheets, newest first: accountId (optional), page, pageSize (max 100, default 50). Each has id, accountId, accountName, fileName, sha256, parsed, imported, duplicates, bad, amount, status imported | undone, activeEntries, createdBy, createdAt, undoneAt")
            .ProducesValidationProblem();

        batches.MapPost("/{id:guid}/undo", UndoAsync)
            .WithName("UndoCostBatch")
            .WithSummary("Undo a depository-charges import: voids its cost entries (nothing is deleted; they stop counting in the net figures) and marks the batch undone; the same sheet can be imported again afterwards. Learned names stay. Undoing twice is a no-op (alreadyUndone, voided 0). 404 portfolio.not_found. Audited with counts and the batch id only")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<ImportPreviewResponse>, ProblemHttpResult>> PreviewAsync(
        HttpContext http,
        IQueryHandler<PreviewImportFileQuery, Result<ImportFilePreview>> handler,
        CancellationToken cancellationToken)
    {
        var upload = await ReportUploads.ReadAsync(http, forImport: false, cancellationToken);
        if (upload.IsFailure)
        {
            return ProblemResults.From(upload.Error, http);
        }

        var previews = new List<ImportFilePreview>(upload.Value.Files.Count);
        foreach (var file in upload.Value.Files)
        {
            if (file.Content is null)
            {
                previews.Add(new ImportFilePreview(file.FileName, "unknown", false, null, null, file.Problem, null, [], null, null, null, null, null));
                continue;
            }

            var preview = await handler.HandleAsync(new PreviewImportFileQuery(upload.Value.AccountId, file.FileName, file.Content, upload.Value.Choices), cancellationToken);
            if (preview.IsFailure)
            {
                return ProblemResults.From(preview.Error, http);
            }

            previews.Add(preview.Value);
        }

        return TypedResults.Ok(new ImportPreviewResponse(previews));
    }

    private static async Task<Results<Ok<ImportRunResponse>, ProblemHttpResult>> ImportAsync(
        HttpContext http,
        ICommandHandler<ImportFileCommand, ImportFileResult> handler,
        CancellationToken cancellationToken)
    {
        var upload = await ReportUploads.ReadAsync(http, forImport: true, cancellationToken);
        if (upload.IsFailure)
        {
            return ProblemResults.From(upload.Error, http);
        }

        var actor = http.GetActor();
        var results = new List<ImportFileResult>(upload.Value.Files.Count);
        for (var i = 0; i < upload.Value.Files.Count; i++)
        {
            var file = upload.Value.Files[i];
            if (file.Content is null)
            {
                results.Add(new ImportFileResult(file.FileName, "unknown", "rejected", file.Problem?.Message, file.Problem, null, null, null, null, 0));
                continue;
            }

            var done = await handler.HandleAsync(
                new ImportFileCommand(
                    upload.Value.AccountId,
                    file.FileName,
                    file.Content,
                    i < upload.Value.Digests.Count ? upload.Value.Digests[i] : null,
                    upload.Value.Rebuild,
                    upload.Value.Choices,
                    upload.Value.AcceptOtherAccount.Contains(i),
                    actor),
                cancellationToken);
            if (done.IsFailure)
            {
                return ProblemResults.From(done.Error, http);
            }

            results.Add(done.Value);
        }

        return TypedResults.Ok(new ImportRunResponse(results));
    }

    private static async Task<Results<Ok<CostBatchPage>, ProblemHttpResult>> ListBatchesAsync(
        HttpContext http,
        IQueryHandler<ListCostBatchesQuery, Result<CostBatchPage>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListCostBatchesQuery(accountId, page, pageSize), cancellationToken), http);

    private static async Task<Results<Ok<CostUndoResult>, ProblemHttpResult>> UndoAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<UndoCostBatchCommand, CostUndoResult> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UndoCostBatchCommand(id, http.GetActor()), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}
