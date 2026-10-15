using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Imports a dividend file (ADR 0028): stores the rows the account does not have yet — those the preview listed as new, needing a match or ambiguous, and the possible duplicates the owner ticked (<c>IncludeDuplicates</c>, by line) — as one batch of
/// dividends with <c>source</c> csv, each with its identity (the same payment is never stored twice, so importing the same file again stores nothing and makes no batch) and the file's quantity. The owner's <c>Choices</c> say what a scrip name means
/// (a holding, an instrument or nothing); a choice with <c>Remember</c> is saved as an alias for the next files. A row left without a holding or instrument is stored unallocated with the file's name; nothing is guessed. <b>The owner must have seen what
/// is stored:</b> <c>Digest</c> is the one the preview returned; when holdings, aliases or dividends changed in a way that matters to a row meanwhile the answer is <c>409 portfolio.preview_stale</c> with the fresh report and nothing is stored. All or
/// nothing, in one unit of work. Audited with counts and ids only.
/// </summary>
public sealed record ImportDividendsCommand(
    Guid AccountId,
    string FileName,
    TradeFileContent Content,
    string? Digest,
    IReadOnlyList<DividendChoice> Choices,
    IReadOnlyList<int> IncludeDuplicates,
    string Actor) : ICommand;

internal sealed class ImportDividendsHandler(
    DividendWorkspace workspace,
    IDividendStore dividends,
    IDividendBatches batches,
    IDividendAliases aliases,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ImportDividendsHandler> logger) : ICommandHandler<ImportDividendsCommand, DividendImportResult>
{
    public async ValueTask<Result<DividendImportResult>> HandleAsync(ImportDividendsCommand command, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        if (string.IsNullOrWhiteSpace(command.Digest))
        {
            return Result.Failure<DividendImportResult>(PortfolioErrors.Invalid("digest", "Required: preview the file first and send its digest, so that only what you saw is imported."));
        }

        if (command.Content.Length > DividendCsvLimits.MaxBytes)
        {
            return Result.Failure<DividendImportResult>(PortfolioErrors.Invalid("file", $"The file is larger than {DividendCsvLimits.MaxBytes / (1024 * 1024)} MB."));
        }

        var account = await workspace.AccountAsync(command.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<DividendImportResult>(account.Error);
        }

        Result<DividendFile> read;
        await using (var stream = command.Content.Open())
        {
            read = await DividendCsvReader.ReadAsync(stream, IndiaTime.Today(clock.GetUtcNow()), cancellationToken);
        }

        if (read.IsFailure)
        {
            return Result.Failure<DividendImportResult>(read.Error);
        }

        var file = read.Value;
        var sha256 = command.Content.Sha256();
        var accountName = account.Value.Name;
        var digest = command.Digest.Trim();
        var includeDuplicates = command.IncludeDuplicates.ToHashSet();
        DividendImportResult? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                var at = clock.GetUtcNow();
                var plan = await workspace.PlanAsync(account.Value, file, token);
                if (!string.Equals(digest, plan.Digest, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(DividendImportErrors.PreviewStale(DividendImportViews.Report(command.FileName, plan)));
                }

                var selected = DividendPlanner.Select(plan, command.Choices, includeDuplicates);
                if (selected.IsFailure)
                {
                    return Result.Failure(selected.Error);
                }

                var selection = selected.Value;
                var batchId = Guid.CreateVersion7(at);
                var fresh = selection.ToStore.Select(s => Create(account.Value.Id, batchId, s, command.Actor, at)).ToList();
                DividendBatch? batch = null;
                if (fresh.Count > 0)
                {
                    var amount = fresh.Sum(d => d.Amount);
                    var notStored = file.Parsed - fresh.Count;
                    batch = DividendBatch.Start(batchId, account.Value.Id, command.FileName, sha256, file.Parsed, fresh.Count, notStored, file.BadTotal, amount, command.Actor, at);
                    batches.Add(batch);
                    dividends.AddRange(fresh);
                    events.Record(AccountEvent.Record(
                        AccountEventType.DividendsImported,
                        account.Value.Id,
                        $"Dividends imported: batch {batchId}, {file.Parsed} valid rows, {fresh.Count} stored, {notStored} not stored, {file.BadTotal} bad",
                        command.Actor,
                        at));
                }

                var saved = await DividendAliasSaver.SaveAsync(aliases, events, account.Value.Id, selection.Aliases, command.Actor, at, token);
                if (!await unitOfWork.TrySaveAsync(token))
                {
                    return Result.Failure(DividendImportErrors.DividendsChanged);
                }

                var allocated = fresh.Count(d => d.HoldingId is not null);
                var instrumentOnly = fresh.Count(d => d.HoldingId is null && d.InstrumentId is not null);
                result = new DividendImportResult(
                    batch is null ? null : DividendImportViews.Batch(new DividendBatchRow(batch, fresh.Count), accountName),
                    fresh.Count,
                    fresh.Sum(d => d.Amount),
                    allocated,
                    instrumentOnly,
                    fresh.Count - allocated - instrumentOnly,
                    selection.AlreadyImported,
                    selection.PossibleDuplicatesSkipped,
                    saved);
                return Result.Success();
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<DividendImportResult>(outcome.Error);
        }

        var done = result!;
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        DividendImportLog.Imported(logger, account.Value.Slug, done.Imported, file.Parsed - done.Imported, done.AliasesSaved, elapsedMs);
        return Result.Success(done);
    }

    private static Dividend Create(Guid accountId, Guid batchId, SelectedDividend selected, string actor, DateTimeOffset at)
    {
        var row = selected.Planned.Row;
        var holding = selected.Match.Holding;
        var share = selected.Match.Share;
        return Dividend.FromFile(
            accountId,
            holding?.Id,
            holding is not null ? holding.InstrumentId : share?.Id,
            holding?.Isin ?? share?.Isin ?? row.Isin,
            holding?.TradingSymbol ?? share?.Symbol ?? row.Symbol,
            row.Name,
            row.PayDate,
            row.Amount,
            row.PerShare,
            row.Quantity,
            batchId,
            selected.Planned.ExternalId,
            actor,
            at);
    }
}
