using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Imports a tradebook (ADR 0023): stores the trades the account does not have yet (identity: the trade id of the file, else a hash of the line; importing the same file again stores nothing and makes no batch) as one batch, and — with
/// <c>Rebuild</c>, the default — rebuilds the lots of every holding whose result is <c>ok</c>. Trades of securities that are not held, of mismatching or ambiguous holdings, are stored but not applied, and reported. <b>Rebuilding replaces lots, so
/// it must be what the owner saw:</b> <c>Digest</c> is the one the preview returned; when holdings, lots, corporate actions or trades changed meanwhile the answer is <c>409 portfolio.preview_stale</c> with the fresh report and nothing is
/// applied. All or nothing, in one unit of work (the lots of each holding are replaced together with the trades; a holding changed by a sync or a quote meanwhile makes the work run again once). <c>409 portfolio.sync_running</c> while a
/// holdings sync of the account runs. Audited with counts only.
/// </summary>
public sealed record ImportTradesCommand(Guid AccountId, string FileName, TradeFileContent Content, bool Rebuild, string? Digest, string Actor) : ICommand;

internal sealed class ImportTradesHandler(
    TradeWorkspace workspace,
    ITradeStore trades,
    ITradeBatches batches,
    IHoldingSyncStore syncs,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ImportTradesHandler> logger) : ICommandHandler<ImportTradesCommand, TradeImportResult>
{
    public async ValueTask<Result<TradeImportResult>> HandleAsync(ImportTradesCommand command, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        if (command.Rebuild && string.IsNullOrWhiteSpace(command.Digest))
        {
            return Result.Failure<TradeImportResult>(PortfolioErrors.Invalid("digest", "Required with rebuild: preview the file first and send its digest, so that only what you saw is applied."));
        }

        if (command.Content.Length > TradeCsvLimits.MaxBytes)
        {
            return Result.Failure<TradeImportResult>(PortfolioErrors.Invalid("file", $"The file is larger than {TradeCsvLimits.MaxBytes / (1024 * 1024)} MB."));
        }

        var account = await workspace.AccountAsync(command.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<TradeImportResult>(account.Error);
        }

        var now = clock.GetUtcNow();
        Result<TradeFile> read;
        await using (var stream = command.Content.Open())
        {
            read = await TradeCsvReader.ReadAsync(stream, IndiaTime.Today(now), cancellationToken);
        }

        if (read.IsFailure)
        {
            return Result.Failure<TradeImportResult>(read.Error);
        }

        var file = read.Value;
        var identified = TradeIdentity.Identify(account.Value.Id, file.Trades);
        var sha256 = command.Content.Sha256();
        var accountName = account.Value.Name;
        TradeImportResult? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                // Holdings are read below inside this transaction; a sync that is running changes them under us, so the owner waits for it (a sync that starts later is caught by the holdings' concurrency token).
                if (await syncs.IsRunningAsync(account.Value.Id, token))
                {
                    return Result.Failure(PortfolioErrors.SyncRunning);
                }

                var at = clock.GetUtcNow();
                var batchId = Guid.CreateVersion7(at);
                var planned = await workspace.PlanAsync(account.Value, identified, batchId, command.Actor, at, token);
                var fresh = planned.NewTrades.ToList();
                var report = TradeViews.Report(command.FileName, file, planned.Plan, planned.AlreadyImported, fresh.Count);
                if (command.Rebuild && !string.Equals(command.Digest!.Trim(), planned.Plan.Digest, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(TradeErrors.PreviewStale(report));
                }

                TradeBatch? batch = null;
                if (fresh.Count > 0)
                {
                    var duplicates = file.Parsed - fresh.Count;
                    batch = TradeBatch.Start(batchId, account.Value.Id, command.FileName, sha256, file.Parsed, fresh.Count, duplicates, file.Skipped, file.BadTotal, command.Actor, at);
                    batches.Add(batch);
                    trades.Add(fresh);
                    events.Record(AccountEvent.Record(
                        AccountEventType.TradesImported,
                        account.Value.Id,
                        $"Trades imported: {file.Parsed} valid rows, {fresh.Count} stored, {duplicates} already imported or repeated, {file.Skipped} skipped, {file.BadTotal} bad",
                        command.Actor,
                        at));
                }

                var applied = command.Rebuild ? workspace.Apply(planned.Plan.Securities.Select(s => s.Rebuild), at) : new RebuildApplied(0, 0, 0, 0, 0);
                workspace.RecordRebuilt(account.Value.Id, applied, command.Actor, at);
                if (!await unitOfWork.TrySaveAsync(token))
                {
                    return Result.Failure(TradeErrors.TradesChanged);
                }

                result = new TradeImportResult(
                    batch is null ? null : TradeViews.Batch(new TradeBatchRow(batch, batch.Imported, fresh.Select(t => t.Isin ?? t.TradingSymbol).Distinct(StringComparer.Ordinal).Count()), accountName),
                    fresh.Count,
                    planned.AlreadyImported,
                    command.Rebuild,
                    applied.Holdings,
                    applied.LotsReplaced,
                    report);
                return Result.Success();
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<TradeImportResult>(outcome.Error);
        }

        var done = result!;
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        TradesLog.Imported(logger, account.Value.Slug, done.Imported, file.Parsed - done.Imported, done.HoldingsRebuilt, elapsedMs);
        return Result.Success(done);
    }
}
