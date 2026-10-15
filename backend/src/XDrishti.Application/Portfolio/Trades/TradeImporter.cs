using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>The trades of a file to plan and import (ADR 0023, 0029): the file's figures, the trades with their identities, where they came from and the <see cref="DigestSalt"/> that goes into the digest for whatever the plan decided that the lot plan does not show (empty for a tradebook).</summary>
/// <param name="File">The file's figures.</param>
/// <param name="Identified">The trades to plan, each with its identity.</param>
/// <param name="Source">Where the trades came from.</param>
/// <param name="DigestSalt">Goes into the digest.</param>
/// <param name="BeforeSave">Collects whatever else is saved with the trades (remembered names), given the time of the import; run just before the save, inside the unit of work.</param>
internal sealed record PreparedTrades(TradeFile File, IReadOnlyList<IdentifiedTrade> Identified, TradeSource Source, string DigestSalt, Func<DateTimeOffset, CancellationToken, ValueTask>? BeforeSave = null);

/// <summary>What an import stored, with the prepared trades it was made from.</summary>
internal sealed record TradesImported(TradeImportResult Result, PreparedTrades Prepared);

/// <summary>The digest of a preview of trades: the digest of the lot plan, together with the salt of whatever else the preview decided.</summary>
internal static class TradeDigests
{
    public static string With(string digest, string salt) =>
        string.IsNullOrEmpty(salt) ? digest : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(digest + "|" + salt)));

    public static TradePreviewReport Salted(TradePreviewReport report, string salt) => string.IsNullOrEmpty(salt) ? report : report with { Digest = With(report.Digest, salt) };
}

/// <summary>
/// Stores the trades of a file in one unit of work (ADR 0023): the part of an import that every kind of trade file shares — the tradebook CSV and the global transaction report. The caller prepares the trades <b>inside the unit of work</b> (what it decides may depend on what is
/// stored by then) and this plans them against the holdings, checks the digest of the preview when lots are to be rebuilt, stores the new trades as one batch and rebuilds the holdings whose result is <c>ok</c>. <c>409 portfolio.sync_running</c> while a holdings sync of the
/// account runs, and while a broker history run (trades or ledger) of the account runs: it stores trades and rebuilds the same holdings. Audited with counts only.
/// </summary>
internal sealed class TradeImporter(
    TradeWorkspace workspace,
    ITradeStore trades,
    ITradeBatches batches,
    SyncGuard guard,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async ValueTask<Result<TradesImported>> ImportAsync(
        BrokerAccount account,
        string fileName,
        string sha256,
        bool rebuild,
        string? digest,
        string actor,
        Func<CancellationToken, ValueTask<Result<PreparedTrades>>> prepare,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(prepare);
        var accountName = account.Name;
        TradesImported? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                // Holdings are read below inside this transaction; a sync that is running changes them under us, and a broker history run stores the same trades and rebuilds the same lots, so the owner waits for it (a sync that starts later is caught by the holdings' concurrency token).
                if (await guard.RunningAsync(account.Id, token))
                {
                    return Result.Failure(PortfolioErrors.SyncRunning);
                }

                var at = clock.GetUtcNow();
                var preparing = await prepare(token);
                if (preparing.IsFailure)
                {
                    return Result.Failure(preparing.Error);
                }

                var prepared = preparing.Value;
                var batchId = Guid.CreateVersion7(at);
                var planned = await workspace.PlanAsync(account, prepared.Identified, batchId, actor, at, prepared.Source, token);
                var fresh = planned.NewTrades.ToList();
                var report = TradeDigests.Salted(TradeViews.Report(fileName, prepared.File, planned.Plan, planned.AlreadyImported, fresh.Count), prepared.DigestSalt);
                if (rebuild && !string.Equals(digest!.Trim(), report.Digest, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(TradeErrors.PreviewStale(report));
                }

                TradeBatch? batch = null;
                if (fresh.Count > 0)
                {
                    var duplicates = prepared.File.Parsed - fresh.Count;
                    batch = TradeBatch.Start(batchId, account.Id, fileName, sha256, prepared.File.Parsed, fresh.Count, duplicates, prepared.File.Skipped, prepared.File.BadTotal, actor, at);
                    batches.Add(batch);
                    trades.Add(fresh);
                    var origin = prepared.Source == TradeSource.Bill ? "Trades imported from a Dhan report" : "Trades imported";
                    events.Record(AccountEvent.Record(
                        AccountEventType.TradesImported,
                        account.Id,
                        $"{origin}: {prepared.File.Parsed} valid rows, {fresh.Count} stored, {duplicates} already imported or repeated, {prepared.File.Skipped} skipped, {prepared.File.BadTotal} bad",
                        actor,
                        at));
                }

                if (prepared.BeforeSave is { } remember)
                {
                    await remember(at, token);
                }

                var applied = rebuild ? workspace.Apply(planned.Plan.Securities.Select(s => s.Rebuild), at) : new RebuildApplied(0, 0, 0, 0, 0);
                workspace.RecordRebuilt(account.Id, applied, actor, at);
                if (!await unitOfWork.TrySaveAsync(token))
                {
                    return Result.Failure(TradeErrors.TradesChanged);
                }

                result = new TradesImported(
                    new TradeImportResult(
                        batch is null ? null : TradeViews.Batch(new TradeBatchRow(batch, batch.Imported, fresh.Select(t => t.Isin ?? t.TradingSymbol).Distinct(StringComparer.Ordinal).Count()), accountName),
                        fresh.Count,
                        planned.AlreadyImported,
                        rebuild,
                        applied.Holdings,
                        applied.LotsReplaced,
                        report),
                    prepared);
                return Result.Success();
            },
            cancellationToken);
        return outcome.IsFailure ? Result.Failure<TradesImported>(outcome.Error) : Result.Success(result!);
    }
}
