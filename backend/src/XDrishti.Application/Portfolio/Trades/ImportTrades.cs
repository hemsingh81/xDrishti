using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Imports a tradebook (ADR 0023): stores the trades the account does not have yet (identity: the trade id of the file, else a hash of the line; importing the same file again stores nothing and makes no batch) as one batch, and — with
/// <c>Rebuild</c>, the default — rebuilds the lots of every holding whose result is <c>ok</c>. Trades of securities that are not held, of mismatching or ambiguous holdings, are stored but not applied, and reported. <b>Rebuilding replaces lots, so
/// it must be what the owner saw:</b> <c>Digest</c> is the one the preview returned; when holdings, lots, corporate actions or trades changed meanwhile the answer is <c>409 portfolio.preview_stale</c> with the fresh report and nothing is
/// applied. All or nothing, in one unit of work (the lots of each holding are replaced together with the trades; a holding changed by a sync or a quote meanwhile makes the work run again once). <c>409 portfolio.sync_running</c> while a
/// holdings sync, or a trades or ledger read of the account, runs. Audited with counts only.
/// </summary>
public sealed record ImportTradesCommand(Guid AccountId, string FileName, TradeFileContent Content, bool Rebuild, string? Digest, string Actor) : ICommand;

internal sealed class ImportTradesHandler(
    TradeWorkspace workspace,
    TradeImporter importer,
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
        var imported = await importer.ImportAsync(
            account.Value,
            command.FileName,
            command.Content.Sha256(),
            command.Rebuild,
            command.Digest,
            command.Actor,
            _ => ValueTask.FromResult(Result.Success(new PreparedTrades(file, identified, TradeSource.Csv, string.Empty))),
            cancellationToken);
        if (imported.IsFailure)
        {
            return Result.Failure<TradeImportResult>(imported.Error);
        }

        var done = imported.Value.Result;
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        TradesLog.Imported(logger, account.Value.Slug, done.Imported, file.Parsed - done.Imported, done.HoldingsRebuilt, elapsedMs);
        return Result.Success(done);
    }
}
