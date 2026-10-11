using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Brings the holdings of a connected Dhan account in line with Dhan (ADR 0012), by hand ("Sync now", <see cref="SyncTrigger.Manual"/>) or by the
/// automatic refresh (<see cref="SyncTrigger.Auto"/>): new holdings get a lot dated today; a larger quantity adds a lot dated today at the implied
/// cost; a smaller quantity or a holding Dhan no longer lists closes lots oldest first at the last price, marked estimated; unchanged holdings only
/// get new prices. Idempotent. One transaction per account: a failure changes nothing and is recorded on the run. One run per account at a time
/// (<c>portfolio.sync_running</c>). <b>Safeguard:</b> an answer that would close all or most of the open holdings (an empty list, or more than half and at
/// least 3) is applied only when <c>Confirm</c> is set; otherwise nothing is changed and a manual run gets <c>portfolio.confirmation_required</c> while an
/// automatic run is recorded as skipped (one record per India day). A confirmation must echo the numbers the owner saw (<c>ExpectedOpenHoldings</c>,
/// <c>ExpectedWouldClose</c>); when Dhan's answer differs now the sync answers <c>portfolio.confirmation_stale</c> with the fresh numbers and changes nothing.
/// <b>An account without holdings</b> is imported instead (ADR 0013): every holding Dhan lists is created with a lot dated with the account's first import date
/// (flagged as default), and the run is recorded with the trigger "import". After an automatic run, old finished sync records (older than 90 days, never failed ones) are pruned in a bounded batch. An automatic run that cannot start (the account is not connected, no bucket to import to) is recorded as skipped (once a day) instead of failing. An automatic first import waits until the account was last changed more than 10 minutes ago (<c>portfolio.settling</c>, nothing recorded), so that per-holding dates chosen in the save that switched the refresh on are not overwritten.
/// </summary>
public sealed record SyncHoldingsCommand(
    Guid AccountId, SyncTrigger Trigger, string Actor, bool Confirm = false, int? ExpectedOpenHoldings = null, int? ExpectedWouldClose = null) : ICommand;

internal sealed class SyncHoldingsHandler(
    HoldingsFetcher fetcher,
    SyncRuns runs,
    HoldingsImporter importer,
    IHoldingStore holdings,
    IHoldingSyncStore syncs,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<SyncHoldingsHandler> logger) : ICommandHandler<SyncHoldingsCommand, SyncRunView>
{
    /// <summary>Finished sync records are kept this many days (failed ones stay).</summary>
    public const int RetentionDays = 90;

    /// <summary>An automatic first import (account without holdings) waits until the account was last changed this long ago (ADR 0013).</summary>
    public static readonly TimeSpan FirstImportSettleTime = TimeSpan.FromMinutes(10);

    /// <summary>The most records one pruning removes.</summary>
    public const int PruneBatch = 500;

    public async ValueTask<Result<SyncRunView>> HandleAsync(SyncHoldingsCommand command, CancellationToken cancellationToken)
    {
        var account = await fetcher.AccountAsync(command.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<SyncRunView>(account.Error);
        }

        if (command.Confirm && (command.ExpectedOpenHoldings is null || command.ExpectedWouldClose is null))
        {
            var errors = new FieldErrors();
            if (command.ExpectedOpenHoldings is null)
            {
                errors.Add("expectedOpenHoldings", "Required when confirming: the number of open holdings from the confirmation request.");
            }

            if (command.ExpectedWouldClose is null)
            {
                errors.Add("expectedWouldClose", "Required when confirming: the number of holdings that would be closed, from the confirmation request.");
            }

            return Result.Failure<SyncRunView>(PortfolioErrors.Invalid(errors));
        }

        var connected = await fetcher.ConnectedAsync(account.Value.Id, cancellationToken);
        if (connected.IsFailure)
        {
            if (command.Trigger == SyncTrigger.Auto)
            {
                await SkipOncePerDayAsync(account.Value, command.Actor, "The account is not connected to Dhan.", cancellationToken);
            }

            return Result.Failure<SyncRunView>(connected.Error);
        }

        var bucket = await fetcher.BucketAsync(account.Value, cancellationToken);
        if (bucket.IsFailure)
        {
            if (command.Trigger == SyncTrigger.Auto)
            {
                await SkipOncePerDayAsync(account.Value, command.Actor, bucket.Error.Message, cancellationToken);
            }

            return Result.Failure<SyncRunView>(bucket.Error);
        }

        if (!await holdings.AnyForAccountAsync(account.Value.Id, cancellationToken))
        {
            // The owner may still be choosing per-holding dates in the save that changed the settings: an automatic first import waits (nothing is recorded).
            if (command.Trigger == SyncTrigger.Auto && account.Value.UpdatedAt > clock.GetUtcNow() - FirstImportSettleTime)
            {
                return Result.Failure<SyncRunView>(PortfolioErrors.Settling);
            }

            return await FirstSyncAsync(account.Value, bucket.Value, command, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var outcome = await runs.RunAsync<SyncCounts>(
            account.Value,
            command.Trigger,
            command.Actor,
            token => fetcher.FetchAsync(account.Value.Id, token),
            async (fetched, token) =>
            {
                var existing = await holdings.ListForAccountAsync(account.Value.Id, token);
                var index = new HoldingIndex(existing);

                // Before anything is changed: would this answer close all or most of what the account holds?
                var listed = fetched.Rows.Select(index.Find).OfType<Holding>().Select(h => h.Id).ToHashSet();
                var open = existing.Count(h => h.Status == HoldingStatus.Open);
                var wouldClose = existing.Count(h => h.Status == HoldingStatus.Open && !listed.Contains(h.Id));
                if (command.Confirm)
                {
                    if (command.ExpectedOpenHoldings != open || command.ExpectedWouldClose != wouldClose)
                    {
                        return Result.Failure<Applied<SyncCounts>>(PortfolioErrors.ConfirmationStale(fetched.Rows.Count, open, wouldClose));
                    }
                }
                else if (IsSuspicious(fetched.Rows.Count, open, wouldClose))
                {
                    return Result.Failure<Applied<SyncCounts>>(PortfolioErrors.ConfirmationRequired(fetched.Rows.Count, open, wouldClose));
                }

                var seen = new HashSet<Guid>();
                var counts = SyncCounts.None;
                foreach (var row in fetched.Rows)
                {
                    var instrument = fetched.Instruments.GetValueOrDefault(row.Key);
                    var match = index.Find(row);
                    if (match is null)
                    {
                        holdings.Add(Holding.Open(account.Value.Id, bucket.Value, instrument?.Id, instrument?.Name, row, today, usesDefaultDate: false, LotSource.Added, now));
                        counts = counts.Count(HoldingChange.Added);
                    }
                    else if (seen.Add(match.Id))
                    {
                        counts = counts.Count(match.Reconcile(row, instrument?.Id, instrument?.Name, today, now));
                    }
                }

                foreach (var gone in existing.Where(h => h.Status == HoldingStatus.Open && !seen.Contains(h.Id)))
                {
                    if (gone.CloseMissing(today, now))
                    {
                        counts = counts.Count(HoldingChange.Closed);
                    }
                }

                // Automatic runs that found nothing but new prices would fill the audit trail every few minutes: only changes are recorded.
                if (command.Trigger != SyncTrigger.Auto || counts.HasChanges)
                {
                    events.Record(AccountEvent.Record(
                        AccountEventType.HoldingsSynced,
                        account.Value.Id,
                        $"Holdings synced ({HoldingViews.Name(command.Trigger)}): {counts.Added} new, {counts.Increased} increased, {counts.Decreased} decreased, {counts.Closed} closed, {counts.Unchanged} unchanged",
                        command.Actor,
                        now));
                }

                return Result.Success(new Applied<SyncCounts>(counts, counts));
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<SyncRunView>(outcome.Error);
        }

        return await FinishedAsync(outcome.Value.RunId, command.Trigger, cancellationToken);
    }

    /// <summary>
    /// The first sync of an account that has no holdings (manual or automatic) is its import (ADR 0013): every holding Dhan lists is created with one lot
    /// dated with the account's first import date and flagged as such (editable per lot); the run is recorded with the trigger "import".
    /// </summary>
    private async ValueTask<Result<SyncRunView>> FirstSyncAsync(BrokerAccount account, Guid bucket, SyncHoldingsCommand command, CancellationToken cancellationToken)
    {
        var imported = await importer.ImportAllAsync(account, bucket, command.Actor, cancellationToken);
        return imported.IsFailure
            ? Result.Failure<SyncRunView>(imported.Error)
            : await FinishedAsync(imported.Value.RunId, command.Trigger, cancellationToken);
    }

    private async ValueTask<Result<SyncRunView>> FinishedAsync(Guid runId, SyncTrigger trigger, CancellationToken cancellationToken)
    {
        if (trigger == SyncTrigger.Auto)
        {
            await PruneAsync(cancellationToken);
        }

        var finished = await syncs.GetAsync(runId, cancellationToken);
        return finished is null ? Result.Failure<SyncRunView>(PortfolioErrors.SyncFailed) : Result.Success(HoldingViews.Run(finished));
    }

    /// <summary>Keeps the table small: finished records older than 90 days that did not fail go, a bounded batch at a time. Never fails the sync.</summary>
    private async ValueTask PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await syncs.PruneAsync(clock.GetUtcNow().AddDays(-RetentionDays), PruneBatch, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // housekeeping after a successful sync: a failure here must not turn it into a failed one
        catch (Exception ex)
#pragma warning restore CA1031
        {
            PortfolioLog.PruneFailed(logger, ex);
        }
    }

    /// <summary>
    /// An empty answer while holdings are open, or one that would close more than half of the open holdings with at least 3 of them (exactly half is
    /// not suspicious; fewer than 3 never is, unless the answer is empty).
    /// </summary>
    internal static bool IsSuspicious(int listedRows, int open, int wouldClose) =>
        open > 0 && (listedRows == 0 || (wouldClose >= 3 && wouldClose * 2 > open));

    /// <summary>Records that an automatic run was not attempted, unless that was recorded for the account today (India) already.</summary>
    private async ValueTask SkipOncePerDayAsync(BrokerAccount account, string actor, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var (start, end) = BrokerTokenExpiry.IndianDay(now);
        if (await syncs.HasSkippedBetweenAsync(account.Id, start, end, cancellationToken))
        {
            return;
        }

        await syncs.RecordSkippedAsync(
            HoldingSync.Skipped(account.Id, SyncTrigger.Auto, actor, reason, now), cancellationToken);
        PortfolioLog.AutoSyncSkipped(logger, account.Slug);
    }
}
