using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Undoes an import (ADR 0023): voids every trade of the batch (nothing is deleted) and rebuilds from the remaining trades the lots of the holdings that were affected and whose lots came from trades — with no trades left, a holding is back to
/// one lot of the broker's quantity and cost dated by the account's first import date, flagged as default. <b>Lots the owner dated by hand or a sync added, which an earlier rebuild replaced, are not brought back</b> (the result counts
/// what the rebuild removes). A holding whose lots did not come from trades (the file was imported without a rebuild) keeps its lots; so does one whose remaining trades do not fit its quantity (listed in <c>NotRebuilt</c>). With
/// <c>DryRun</c> nothing is changed and the same result says what an undo would do. Undoing a batch again is a no-op. <c>409 portfolio.sync_running</c> while a holdings sync of the account runs (not for a dry run). Audited with counts only.
/// </summary>
public sealed record UndoTradeBatchCommand(Guid BatchId, string Actor, bool DryRun = false) : ICommand;

internal sealed class UndoTradeBatchHandler(
    TradeWorkspace workspace,
    ITradeBatches batches,
    ITradeStore trades,
    IHoldingStore holdings,
    IHoldingSyncStore syncs,
    IAccountStore accounts,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<UndoTradeBatchHandler> logger) : ICommandHandler<UndoTradeBatchCommand, TradeUndoResult>
{
    public async ValueTask<Result<TradeUndoResult>> HandleAsync(UndoTradeBatchCommand command, CancellationToken cancellationToken)
    {
        var found = await batches.GetAsync(command.BatchId, cancellationToken);
        if (found is null)
        {
            return Result.Failure<TradeUndoResult>(PortfolioErrors.TradeBatchNotFound);
        }

        var account = await accounts.FindReadOnlyAsync(found.Batch.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<TradeUndoResult>(PortfolioErrors.AccountNotFound);
        }

        if (command.DryRun)
        {
            if (found.Batch.Status == TradeBatchStatus.Undone)
            {
                return Result.Success(Done(TradeViews.Batch(found, account.Name), alreadyUndone: true, dryRun: true, applied: false, voided: 0, new UndoOutcome([], new RebuildApplied(0, 0, 0, 0, 0), [])));
            }

            var preview = await PlanAsync(found.Batch, account.Portfolio.ImportDate, cancellationToken);
            return Result.Success(Done(TradeViews.Batch(found, account.Name), alreadyUndone: false, dryRun: true, applied: false, preview.Voided, preview.Outcome with { Applied = Counts(preview.Outcome.Rebuilds) }));
        }

        TradeUndoResult? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                var batch = await batches.FindAsync(command.BatchId, token);
                if (batch is null)
                {
                    return Result.Failure(PortfolioErrors.TradeBatchNotFound);
                }

                if (batch.Status == TradeBatchStatus.Undone)
                {
                    var row = await batches.GetAsync(batch.Id, token);
                    result = Done(TradeViews.Batch(row!, account.Name), alreadyUndone: true, dryRun: false, applied: false, voided: 0, new UndoOutcome([], new RebuildApplied(0, 0, 0, 0, 0), []));
                    return Result.Success();
                }

                if (await syncs.IsRunningAsync(batch.AccountId, token))
                {
                    return Result.Failure(PortfolioErrors.SyncRunning);
                }

                var now = clock.GetUtcNow();
                var plan = await PlanAsync(batch, account.Portfolio.ImportDate, token);
                var voided = await trades.VoidBatchAsync(batch.Id, command.Actor, now, token);
                batch.Undo(now);
                var applied = workspace.Apply(plan.Outcome.Rebuilds, now);
                events.Record(AccountEvent.Record(
                    AccountEventType.TradesUndone,
                    batch.AccountId,
                    $"Trade import undone: {voided} trades voided, {applied.Holdings} holdings rebuilt, {plan.Outcome.NotRebuilt.Count} not rebuilt",
                    command.Actor,
                    now));
                workspace.RecordRebuilt(batch.AccountId, applied, command.Actor, now);
                await unitOfWork.SaveAsync(token);
                var fresh = await batches.GetAsync(batch.Id, token);
                result = Done(TradeViews.Batch(fresh!, account.Name), alreadyUndone: false, dryRun: false, applied: true, voided, plan.Outcome with { Applied = applied });
                return Result.Success();
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<TradeUndoResult>(outcome.Error);
        }

        var done = result!;
        if (done.Applied)
        {
            TradesLog.Undone(logger, command.BatchId, account.Slug, done.Voided, done.HoldingsRebuilt);
        }

        return Result.Success(done);
    }

    /// <summary>
    /// Works out what undoing the batch does to the holdings it touched, reading only their securities' trades: the affected holdings whose lots came from trades are rebuilt from the trades that remain (the batch's are left out);
    /// the ones that cannot be (a mismatch, an ambiguous security) are listed.
    /// </summary>
    private async ValueTask<(int Voided, UndoOutcome Outcome)> PlanAsync(TradeBatch batch, DateOnly firstImportDate, CancellationToken cancellationToken)
    {
        var all = await holdings.ListForAccountAsync(batch.AccountId, cancellationToken);
        var mine = await trades.ListActiveOfBatchAsync(batch.Id, cancellationToken);
        var matcher = new HoldingMatcher(all);
        var affected = HoldingRebuilds.Group(matcher, mine).Keys.ToHashSet();
        var held = all.Where(h => affected.Contains(h.Id) && h.HasTradeLots && h.Status == HoldingStatus.Open).OrderBy(h => h.TradingSymbol, StringComparer.Ordinal).ToList();
        var actions = await workspace.ActionsOfAsync(held, cancellationToken);
        var rebuilds = new List<HoldingRebuild>();
        var notRebuilt = new List<TradeNotRebuilt>();
        foreach (var holding in held)
        {
            if (matcher.Find(holding.Isin, holding.TradingSymbol) is not { Kind: MatchKind.One })
            {
                notRebuilt.Add(new TradeNotRebuilt(holding.TradingSymbol, holding.Id, RebuildStatuses.Ambiguous));
                continue;
            }

            var candidates = await trades.ListActiveForSecurityAsync(batch.AccountId, TradeRules.NormaliseIsin(holding.Isin), TradeRules.NormaliseSymbol(holding.TradingSymbol), cancellationToken);
            var remaining = HoldingRebuilds.Of(matcher, holding, candidates.Where(t => t.BatchId != batch.Id));
            var rebuild = HoldingRebuilds.Compute(holding, remaining, holding.InstrumentId is { } instrument ? actions.GetValueOrDefault(instrument) ?? [] : [], firstImportDate);
            rebuilds.Add(rebuild);
            if (rebuild.Status == RebuildStatuses.QuantityMismatch)
            {
                notRebuilt.Add(new TradeNotRebuilt(holding.TradingSymbol, holding.Id, rebuild.Reason ?? RebuildStatuses.QuantityMismatch));
            }
        }

        // Trades of the batch that more than one holding matches cannot be traced to a holding: they are listed too.
        foreach (var group in mine.Where(t => matcher.Find(t.Isin, t.TradingSymbol).Kind == MatchKind.Ambiguous).GroupBy(t => t.Isin ?? t.TradingSymbol, StringComparer.Ordinal))
        {
            notRebuilt.Add(new TradeNotRebuilt(group.First().TradingSymbol.Length > 0 ? group.First().TradingSymbol : group.Key, null, RebuildStatuses.Ambiguous));
        }

        return (mine.Count, new UndoOutcome(rebuilds, new RebuildApplied(0, 0, 0, 0, 0), notRebuilt));
    }

    private static RebuildApplied Counts(IEnumerable<HoldingRebuild> rebuilds)
    {
        var replacing = rebuilds.Where(r => r.WillReplace).ToList();
        return new RebuildApplied(replacing.Count, replacing.Sum(r => r.LotsBefore), replacing.Sum(r => r.LotsAfter), replacing.Sum(r => r.HandDatedLotsReplaced), replacing.Sum(r => r.SyncLotsReplaced));
    }

    private static TradeUndoResult Done(TradeBatchView batch, bool alreadyUndone, bool dryRun, bool applied, int voided, UndoOutcome outcome) => new(
        batch,
        alreadyUndone,
        voided,
        outcome.Applied.Holdings,
        outcome.Applied.LotsReplaced,
        outcome.NotRebuilt.Count,
        dryRun,
        applied,
        outcome.Applied.HandDated,
        outcome.Applied.Sync,
        outcome.NotRebuilt);

    /// <summary>The holdings to rebuild, what was (or would be) replaced, and the ones that cannot be rebuilt.</summary>
    private sealed record UndoOutcome(IReadOnlyList<HoldingRebuild> Rebuilds, RebuildApplied Applied, IReadOnlyList<TradeNotRebuilt> NotRebuilt);
}
