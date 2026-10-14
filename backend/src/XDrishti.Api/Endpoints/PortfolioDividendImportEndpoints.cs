using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Dividends from a payout file (ADR 0028, P2-15): the preview and the import of a file (matching its scrip names to holdings, with remembered names), the import history and Undo. All routes need a session; every POST also needs the CSRF token; none needs a
/// step-up (no secrets are involved and an import is undone by voiding).
/// </summary>
internal sealed class PortfolioDividendImportEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var dividends = api.MapGroup("/portfolio/dividends").WithTags("Dividends");

        dividends.MapPost("/preview", PreviewAsync)
            .WithName("PreviewDividendImport")
            .WithSummary("Read a dividend payout file against an account and say what importing it would do, storing and changing nothing. Body: multipart/form-data with accountId (an account with the Portfolio role) and file (CSV, UTF-8, at most 10 MB and 20,000 data rows; header: a date, a scrip name or symbol or ISIN, and the amount paid (Dividend Paid, Dividend Amount, Payout; a plain Amount or Net Amount only beside a Dividend Per Share or DPS column) - for example Date, Scrip Name, Dividend Per Share, Quantity, Dividend Paid - names accepted in any case with punctuation ignored; dates like 13 Jun 2024, 23 Sept 2024, 2024-06-13, 13-06-2024; amounts like 1,134.00). Returns format dividends, the file (rows, parsed, parsedAmount, repeated and identicalRepeats - rows identical to an earlier row are counted once -, distinct, bad rows with line and reason - at most 200 listed, badTotal counts all -, alreadyImported, possibleDuplicates, toAdd, needsMatch, ambiguous), the items (line, payDate, name, nameKey - the name as the server compares names: two spellings of one company have one nameKey, so group rows and key choices by it -, perShare, quantity, amount, status new | imported | possible_duplicate | needs_match | ambiguous, match {kind alias | isin | symbol | name | contains | master | choice | none, holdingId, instrumentId, symbol, isin, name, candidates}, warnings amount_mismatch | before_first_lot | partial_name_match), totals {count, amount, byYear} of what would be added, beforeFirstLot {count, amount, firstLotDate} and the digest. 400 portfolio.wrong_file_kind (extension detected: trades | tradebook_without_sides for a tradebook, statement for a file of a date, a name and a plain Amount with nothing in the header that says dividend - the kind is judged from the header before any column is read); 400 portfolio.invalid (field file: unreadable, too big, missing columns - with the accepted names -, not UTF-8; accountId); 404 portfolio.not_found (account); 409 portfolio.role_off")
            .Accepts<DividendUploadForm>("multipart/form-data")
            .ProducesRejections(conflict: true);

        dividends.MapPost("/import", ImportAsync)
            .WithName("ImportDividends")
            .WithSummary("Import a dividend payout file: body multipart/form-data with accountId, file, digest (required: the digest of the preview), choices (optional JSON array of {name, holdingId | instrumentId | none, remember}: what a scrip name of the file means - name is the name as written or the item's nameKey; choices for spellings with one nameKey that agree are one choice, ones that disagree are a 400; remember saves it as an alias for later files) and includeDuplicates (optional JSON array of the lines of possible duplicates to import anyway). Stores the rows that are to be added as one batch of dividends with source csv, each with an identity (account, date, normalised name, per share, quantity, amount - the same file again stores nothing and makes no batch; rows identical within a file count once), the file's quantity and, when no holding is matched, the file's scrip name (unallocated; nothing is guessed). Answers imported, amount, allocated, instrumentOnly, unallocated, alreadyImported, possibleDuplicatesSkipped, aliasesSaved and the batch (preview again for the report). 409 portfolio.preview_stale when holdings, remembered names or dividends changed since the preview (extension report: the fresh preview; nothing stored); 400 portfolio.invalid (digest; choices naming no row of the file or no holding of the account, or contradicting each other for one nameKey; form fields too long or too many); 400 portfolio.wrong_file_kind (as for the preview); 404 portfolio.not_found. Audited with counts and ids only")
            .Accepts<DividendUploadForm>("multipart/form-data")
            .ProducesRejections(conflict: true);

        var batches = api.MapGroup("/portfolio/dividend-batches").WithTags("Dividends");

        batches.MapGet("/", ListBatchesAsync)
            .WithName("ListDividendBatches")
            .WithSummary("The imports of dividend files, newest first: accountId (optional), page, pageSize (max 100, default 50). Each has id, accountId, accountName, fileName, sha256, parsed, imported, duplicates, bad, amount, status imported | undone, activeDividends, createdBy, createdAt, undoneAt")
            .ProducesValidationProblem();

        batches.MapPost("/{id:guid}/undo", UndoAsync)
            .WithName("UndoDividendBatch")
            .WithSummary("Undo an import: voids its dividends (nothing is deleted; they stop counting in every figure) and marks the batch undone; the same file can be imported again afterwards. Remembered names stay. Undoing twice is a no-op (alreadyUndone, voided 0). 404 portfolio.not_found. Audited with counts and the batch id only")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<DividendPreviewReport>, ProblemHttpResult>> PreviewAsync(
        HttpContext http,
        IQueryHandler<PreviewDividendsQuery, Result<DividendPreviewReport>> handler,
        CancellationToken cancellationToken)
    {
        var upload = await DividendUploads.ReadAsync(http, forImport: false, cancellationToken);
        return upload.IsFailure
            ? ProblemResults.From(upload.Error, http)
            : Respond(await handler.HandleAsync(new PreviewDividendsQuery(upload.Value.AccountId, upload.Value.FileName, upload.Value.Content), cancellationToken), http);
    }

    private static async Task<Results<Ok<DividendImportResult>, ProblemHttpResult>> ImportAsync(
        HttpContext http,
        ICommandHandler<ImportDividendsCommand, DividendImportResult> handler,
        CancellationToken cancellationToken)
    {
        var upload = await DividendUploads.ReadAsync(http, forImport: true, cancellationToken);
        return upload.IsFailure
            ? ProblemResults.From(upload.Error, http)
            : Respond(
                await handler.HandleAsync(
                    new ImportDividendsCommand(
                        upload.Value.AccountId, upload.Value.FileName, upload.Value.Content, upload.Value.Digest, upload.Value.Choices, upload.Value.IncludeDuplicates, http.GetActor()),
                    cancellationToken),
                http);
    }

    private static async Task<Results<Ok<DividendBatchPage>, ProblemHttpResult>> ListBatchesAsync(
        HttpContext http,
        IQueryHandler<ListDividendBatchesQuery, Result<DividendBatchPage>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListDividendBatchesQuery(accountId, page, pageSize), cancellationToken), http);

    private static async Task<Results<Ok<DividendUndoResult>, ProblemHttpResult>> UndoAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<UndoDividendBatchCommand, DividendUndoResult> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UndoDividendBatchCommand(id, http.GetActor()), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}
