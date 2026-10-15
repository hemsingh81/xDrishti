using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// Carries out a broker history run (ADR 0025): the dates are cut into windows of at most 30 days; each window is read from Dhan, then stored in <b>one unit of work</b> together with the run's progress and the watermark move, so a failure
/// part-way leaves the watermark where the last whole window put it and the next run reads the failed window again (the identities make that harmless). Trades first: after the last window the lots of the holdings are rebuilt automatically
/// (<see cref="BrokerLotRebuilder"/>) - only when every window was read and the dates reach back to what was read before (a first read from a chosen date is like a tradebook that starts late: what is older is the opening lot). A start date that leaves a hole after the watermark
/// is refused before a run starts (<see cref="BrokerHistoryAccess.CheckFromAsync"/>); the pipeline keeps a defensive guard all the same and skips the rebuild then, because a history with a hole in the middle would put the missing shares into the opening lot. Then the ledger. A run that
/// fails is recorded as failed with a short safe reason and the counts so far; the data is exactly as the last whole window left it. <b>Every way out ends the run</b>: whatever is thrown - also outside the window loop, or by the shutdown - the
/// run is completed as failed (never left <c>running</c>, which would block the account until the cleanup), with a plain text and a token that cannot be cancelled. Dhan's rows are never logged.
/// <para>
/// <b>Identities:</b> a Dhan trade is stored as <c>t:yyyyMMdd:</c> + its exchange trade id, which is what the CSV reader gives a tradebook line that has a trade id, so the two never double. A tradebook <i>without</i> a trade id column is stored under
/// a hash of the line (<c>h:</c>, ADR 0023), which cannot be matched with Dhan's trade id: reading the same trade from Dhan afterwards stores it a second time (and the rebuild then sees both). Import tradebooks with their trade ids, or void the
/// import (Undo) before the first broker read. <b>Intraday</b> products (INTRADAY, INTRA, MIS, CO, BO) are not stored (see <see cref="BrokerSkipReasons.Intraday"/>): they net out within the day and never reach the holdings; every other NSE_EQ product (CNC, MTF, MARGIN) is.
/// </para>
/// </summary>
internal sealed class BrokerHistoryPipeline(
    BrokerHistoryReader reader,
    BrokerLotRebuilder rebuilder,
    IBrokerSyncStore runs,
    IHoldingStore holdings,
    ITradeStore trades,
    ITradeReferenceData reference,
    ILedgerStore ledger,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    ISyncHealthStore health,
    SyncHealthRecorder recorder,
    IHealSettings healSettings,
    TimeProvider clock,
    ILogger<BrokerHistoryPipeline> logger)
{
    /// <summary>A run that has been "running" this long was interrupted (a 12-month backfill takes minutes).</summary>
    public static readonly TimeSpan StaleAfter = SyncLimits.BrokerHistoryStaleAfter;

    /// <summary>Recorded as the actor of scheduled runs and of the automatic rebuilds.</summary>
    public const string SystemActor = "system:broker-history";

    /// <summary>The lots a rebuild makes are stamped a microsecond apart from its start (each after the other); the margin keeps the previous run's own rebuild from looking like a holding that appeared after it. A holdings sync needs longer than this to even reach Dhan.</summary>
    private static readonly TimeSpan RebuiltLotsMargin = TimeSpan.FromSeconds(1);

    private const string Interrupted = "The sync was interrupted because the application is stopping.";

    /// <summary>The window's work: read it, store it, and give the new counts.</summary>
    private delegate ValueTask<Result<BrokerSyncCounts>> WindowWork(DateWindow window, BrokerSyncCounts counts, CancellationToken cancellationToken);

    /// <summary>
    /// Registers a run of <paramref name="kind"/> for the dates from <paramref name="requestedFrom"/> (else the watermark, else the account's first import date) to today (India). <c>portfolio.sync_running</c> when a holdings sync or another
    /// broker history run of the account is running.
    /// </summary>
    public async ValueTask<Result<BrokerSyncRun>> BeginAsync(BrokerAccount account, BrokerSyncKind kind, bool scheduled, DateOnly? requestedFrom, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var now = clock.GetUtcNow();
        var watermark = await runs.GetWatermarkAsync(account.Id, kind, cancellationToken);
        var from = requestedFrom ?? watermark ?? account.Portfolio.ImportDate;
        var trigger = !scheduled ? BrokerSyncTrigger.Manual : watermark is null ? BrokerSyncTrigger.First : BrokerSyncTrigger.Scheduled;

        // The number of this attempt within the day's occurrence: after a failure earlier today it is the next one (a quiet retry, ADR 0027).
        var failing = await health.GetAsync(account.Id, SyncFailures.KindOf(kind), cancellationToken);
        var attempt = failing is { LastFailedAt: { } failedAt } && failing.IsFailing && IndiaTime.Today(failedAt) == IndiaTime.Today(now) ? failing.Attempts + 1 : 1;
        var run = BrokerSyncRun.Start(account.Id, kind, trigger, from, IndiaTime.Today(now), actor, now, attempt);
        return await runs.TryStartAsync(run, StaleAfter, now, cancellationToken) ? Result.Success(run) : Result.Failure<BrokerSyncRun>(PortfolioErrors.SyncRunning);
    }

    /// <summary>Records that a scheduled run was not attempted (the token has expired).</summary>
    public async ValueTask RecordSkippedAsync(BrokerAccount account, string reason, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var now = clock.GetUtcNow();
        await runs.RecordSkippedAsync(BrokerSyncRun.Skipped(account.Id, BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, IndiaTime.Today(now), actor, reason, now), cancellationToken);

        // Neither the trades nor the ledger were read: the owner has to sign in again, and the reads catch up by themselves afterwards (ADR 0027).
        await recorder.FailedAsync(account.Id, SyncHealthKind.Trades, SyncFailures.TokenExpired, cancellationToken);
        await recorder.FailedAsync(account.Id, SyncHealthKind.Ledger, SyncFailures.TokenExpired, cancellationToken);
    }

    /// <summary>
    /// Carries out the trades run that <see cref="BeginAsync"/> registered and, when it succeeded, the ledger run. Never throws for a failure of Dhan or of the data: it is recorded on the runs. Whatever else goes wrong, the runs end as failed; when the
    /// application is stopping they end as failed ("interrupted") and the cancellation is passed on.
    /// </summary>
    public async ValueTask<BrokerHistoryRunView> CarryOutAsync(
        BrokerAccount account, SecretValue token, BrokerSyncRun tradesRun, DateOnly? requestedFrom, bool scheduled, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(tradesRun);
        // The ledger run is assigned as soon as it is registered - before it is executed - so that whatever is thrown while it runs, the catch handlers below end it too (EndAsync leaves a run that has ended alone).
        BrokerSyncRun? ledgerRun = null;
        try
        {
            var watermark = await runs.GetWatermarkAsync(account.Id, BrokerSyncKind.Trades, cancellationToken);
            // Defensive: "Sync now" refuses a start date that leaves a hole after the watermark (BrokerHistoryAccess.CheckFromAsync), so this is true for every run that comes through the use cases; a history with a hole in the middle must never be rebuilt.
            var contiguous = watermark is null || tradesRun.FromDate <= watermark.Value.AddDays(1);
            var tradesOk = await ExecuteAsync(account, tradesRun, actor, scheduled, rebuild: contiguous, TradeWork(account, token, tradesRun, actor), cancellationToken);
            if (tradesOk)
            {
                ledgerRun = await BeginLedgerAsync(account, requestedFrom, scheduled, actor, cancellationToken);
                if (ledgerRun.IsRunning)
                {
                    await ExecuteAsync(account, ledgerRun, actor, scheduled, rebuild: false, LedgerWork(account, token, ledgerRun), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await EndAsync(tradesRun.Id, Interrupted, SyncFailures.Interrupted);
            if (ledgerRun is not null)
            {
                await EndAsync(ledgerRun.Id, Interrupted, SyncFailures.Interrupted);
            }

            throw;
        }
#pragma warning disable CA1031 // any failure must end the run records as failed, never leave them running
        catch (Exception ex)
#pragma warning restore CA1031
        {
            BrokerHistoryLog.Crashed(logger, BrokerHistoryViews.Name(tradesRun.Kind), tradesRun.Id, ex);
            await EndAsync(tradesRun.Id, PortfolioErrors.SyncFailed);
            if (ledgerRun is not null)
            {
                await EndAsync(ledgerRun.Id, PortfolioErrors.SyncFailed);
            }
        }

        var trades = await runs.GetAsync(tradesRun.Id, cancellationToken) ?? tradesRun;
        return new BrokerHistoryRunView(BrokerHistoryViews.Run(trades), ledgerRun is null ? null : BrokerHistoryViews.Run(await runs.GetAsync(ledgerRun.Id, cancellationToken) ?? ledgerRun));
    }

    /// <summary>Ends a run that is still running because its caller failed with <paramref name="cause"/> before or outside the pipeline (the account could not be read, the use case crashed). Never throws.</summary>
    public async ValueTask CrashedAsync(Guid runId, Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        BrokerHistoryLog.Crashed(logger, "run", runId, cause);
        await EndAsync(runId, PortfolioErrors.SyncFailed);
    }

    /// <summary>Ends a run that is still running because the application is stopping: failed, "interrupted"; the next run reads from the watermark. Never throws.</summary>
    public ValueTask InterruptedAsync(Guid runId) => EndAsync(runId, Interrupted, SyncFailures.Interrupted);

    /// <summary>Ends a run that is still running because of <paramref name="error"/> (an account or token that cannot be used any more); the reason code is the error's cause, not its text. Never throws.</summary>
    public ValueTask EndAsync(Guid runId, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return EndAsync(runId, error.Message, SyncFailures.Of(error));
    }

    /// <summary>Ends a run that is still running with a plain sentence and the reason code (one of <see cref="SyncFailures"/>) it is recorded under. Never throws.</summary>
    private async ValueTask EndAsync(Guid runId, string reason, string code)
    {
        try
        {
            // The token that cannot be cancelled: this must work while the application is stopping. The counts so far stay.
            var current = await runs.GetAsync(runId, CancellationToken.None);
            if (current is { IsRunning: true })
            {
                await runs.CompleteAsync(runId, SyncStatus.Failed, current.Counts with { FailureReason = code }, reason, clock.GetUtcNow(), CancellationToken.None);
                await recorder.FailedAsync(current.AccountId, SyncFailures.KindOf(current.Kind), code, CancellationToken.None);
            }
        }
#pragma warning disable CA1031 // nothing more can be done; the cleanup of interrupted syncs ends the run at the next start
        catch (Exception ex)
#pragma warning restore CA1031
        {
            BrokerHistoryLog.AbandonFailed(logger, runId, ex);
        }
    }

    /// <summary>Registers the ledger run after the trades run: the running run, or - when another sync of the account started in between - a skipped record (the ledger waits for the next run; its watermark did not move).</summary>
    private async ValueTask<BrokerSyncRun> BeginLedgerAsync(BrokerAccount account, DateOnly? requestedFrom, bool scheduled, string actor, CancellationToken cancellationToken)
    {
        var begun = await BeginAsync(account, BrokerSyncKind.Ledger, scheduled, requestedFrom, actor, cancellationToken);
        if (begun.IsSuccess)
        {
            return begun.Value;
        }

        var now = clock.GetUtcNow();
        var skipped = BrokerSyncRun.Skipped(account.Id, BrokerSyncKind.Ledger, scheduled ? BrokerSyncTrigger.Scheduled : BrokerSyncTrigger.Manual, IndiaTime.Today(now), actor, "Another sync of this account started; the ledger is read by the next run.", now);
        await runs.RecordSkippedAsync(skipped, cancellationToken);
        return skipped;
    }

    private async ValueTask<bool> ExecuteAsync(BrokerAccount account, BrokerSyncRun run, string actor, bool scheduled, bool rebuild, WindowWork work, CancellationToken cancellationToken)
    {
        var kind = BrokerHistoryViews.Name(run.Kind);
        var counts = BrokerSyncCounts.None with { Attempts = Math.Max(1, run.Counts.Attempts) };
        try
        {
            var windows = BrokerWindows.Split(run.FromDate, run.ToDate);
            var healthKind = SyncFailures.KindOf(run.Kind);
            var stored = await health.GetAsync(account.Id, healthKind, cancellationToken);

            // Windows that dropped rows for a data reason are read again once the Dhan mapping has changed since the account was last read (ADR 0027); they come first, the regular windows follow. The windows an earlier failed attempt
            // finished are not read again (ADR 0030): each window is marked done in the unit of work that stores it.
            var version = reader.MapperVersion(run.Kind);
            var reread = await RereadWindowsAsync(account, run, stored, version, cancellationToken);
            var again = reread.Windows;
            counts = counts with { WindowsTotal = again.Count + windows.Count, RereadVersion = reread.Done.Count > 0 || again.Count > 0 ? version : 0, RereadDone = reread.Done };
            var trigger = BrokerHistoryViews.Name(run.Trigger);
            BrokerHistoryLog.Started(logger, kind, run.Id, account.Slug, trigger, counts.WindowsTotal);
            await runs.UpdateProgressAsync(run.Id, counts, cancellationToken);

            // The mapping version is stamped as soon as the windows to read again have been read - not at the end: a transient failure later in the run must not make every retry read the same windows again. When some of the windows to read again lie
            // inside the run's own dates (a manual run from an earlier date) they are left to the run, so the version waits for its last window: a failure part-way must not drop the windows it never reached.
            var unstamped = stored.MapperVersion < version;
            var stampAt = again.Count + (reread.InsideRun ? windows.Count : 0);
            if (unstamped && stampAt == 0)
            {
                await recorder.MapperVersionAsync(account.Id, healthKind, version, cancellationToken);
                unstamped = false;
            }

            var done = 0;
            foreach (var window in again.Concat(windows))
            {
                // A window read again is added to the done list handed to its unit of work: it is stored together with the window, or not at all.
                var given = done < again.Count ? counts with { RereadDone = [.. counts.RereadDone, new BrokerWindowSpan(window.From, window.To)] } : counts;
                var next = await work(window, given, cancellationToken);
                if (next.IsFailure)
                {
                    return await FailAsync(account, run, counts, next.Error);
                }

                done++;
                if (unstamped && done == stampAt)
                {
                    await recorder.MapperVersionAsync(account.Id, healthKind, version, cancellationToken);
                    unstamped = false;
                }

                if (next.Value.Halved > counts.Halved)
                {
                    // The run says so through its counts; the log has the position.
                    BrokerHistoryLog.Halved(logger, next.Value.WindowsDone, next.Value.WindowsTotal, run.Id);
                }

                counts = next.Value;
            }

            if (rebuild && await RebuildOwedAsync(account.Id, run, counts, cancellationToken))
            {
                var summary = await rebuilder.RebuildAsync(account, SystemActor, cancellationToken);
                counts = summary.AddTo(counts);
                if (scheduled && run.Kind == BrokerSyncKind.Trades)
                {
                    counts = await DeepenAsync(account, counts, summary, work, cancellationToken);
                }
            }

            var finished = await FinishAsync(account, run, counts, scheduled, actor, cancellationToken);
            if (finished.IsFailure)
            {
                return await FailAsync(account, run, counts, finished.Error);
            }

            await recorder.SucceededAsync(account.Id, healthKind, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await runs.CompleteAsync(run.Id, SyncStatus.Failed, counts with { FailureReason = SyncFailures.Interrupted }, Interrupted, clock.GetUtcNow(), CancellationToken.None);
            await recorder.FailedAsync(account.Id, SyncFailures.KindOf(run.Kind), SyncFailures.Interrupted, CancellationToken.None);
            throw;
        }
#pragma warning disable CA1031 // any failure must end the run record as failed, never leave it running
        catch (Exception ex)
#pragma warning restore CA1031
        {
            BrokerHistoryLog.Crashed(logger, kind, run.Id, ex);
            return await FailAsync(account, run, counts, PortfolioErrors.SyncFailed);
        }
    }

    /// <summary>
    /// The windows to read again before the run's own: when the stored mapper version of the account's kind is older than the reader's, the windows that dropped rows for a data reason and were not read since (<see cref="ISyncHealthStore.ListRereadAsync"/>); an
    /// account the first version of this rule has not seen (version 0) also gets the dates of its earlier runs that dropped rows, because windows were not noted before. Dates that a failed attempt before this run already read again (its <see cref="BrokerSyncCounts.RereadDone"/>,
    /// same mapper version) are left out, so a retry after a late failure reads only what is not done (ADR 0030). Windows inside the run's own dates are read anyway (<see cref="RereadWindows.InsideRun"/> tells, so that the version is stamped only when the run has read them).
    /// Nothing when the version is current.
    /// </summary>
    private async ValueTask<RereadWindows> RereadWindowsAsync(BrokerAccount account, BrokerSyncRun run, SyncHealth stored, int current, CancellationToken cancellationToken)
    {
        if (stored.MapperVersion >= current)
        {
            return new RereadWindows([], false, []);
        }

        var ranges = (await health.ListRereadAsync(account.Id, SyncFailures.KindOf(run.Kind), cancellationToken)).ToList();
        if (stored.MapperVersion == 0)
        {
            ranges.AddRange(await runs.ListDroppedRangesAsync(account.Id, run.Kind, cancellationToken));
        }

        var before = await runs.GetPreviousFinishedAsync(account.Id, run.Kind, run.Id, cancellationToken);
        IReadOnlyList<BrokerWindowSpan> carried = before is { Status: SyncStatus.Failed } && before.Counts.RereadVersion == current ? before.Counts.RereadDone : [];
        var noted = Merge(Without(ranges, carried)).SelectMany(r => BrokerWindows.Split(r.From, r.To)).ToList();
        bool Inside(DateWindow w) => w.From >= run.FromDate && w.To <= run.ToDate;
        var windows = noted.Where(w => !Inside(w)).ToList();
        if (windows.Count > 0)
        {
            HealLog.Rereading(logger, account.Slug, windows.Count, current);
        }

        return new RereadWindows(windows, noted.Any(Inside), carried);
    }

    /// <summary>The windows to read before the run's own, whether other windows to read again lie inside the run's own dates, and the windows an earlier failed attempt finished.</summary>
    private sealed record RereadWindows(IReadOnlyList<DateWindow> Windows, bool InsideRun, IReadOnlyList<BrokerWindowSpan> Done);

    /// <summary>The ranges without the dates of the windows that are done (what is left of a range that a done window covers in part stays).</summary>
    internal static IEnumerable<BrokerDateRange> Without(IEnumerable<BrokerDateRange> ranges, IReadOnlyList<BrokerWindowSpan> done)
    {
        var ordered = done.OrderBy(d => d.From).ToList();
        foreach (var range in ranges)
        {
            var from = range.From;
            foreach (var span in ordered)
            {
                if (span.To < from || span.From > range.To)
                {
                    continue;
                }

                if (span.From > from)
                {
                    yield return new BrokerDateRange(from, span.From.AddDays(-1));
                }

                from = span.To.AddDays(1);
                if (from > range.To)
                {
                    break;
                }
            }

            if (from <= range.To)
            {
                yield return new BrokerDateRange(from, range.To);
            }
        }
    }

    /// <summary>The ranges with overlapping or adjoining ones joined, oldest first.</summary>
    internal static IReadOnlyList<BrokerDateRange> Merge(IEnumerable<BrokerDateRange> ranges)
    {
        var merged = new List<BrokerDateRange>();
        foreach (var range in ranges.OrderBy(r => r.From).ThenBy(r => r.To))
        {
            if (merged.Count > 0 && range.From <= merged[^1].To.AddDays(1))
            {
                merged[^1] = new BrokerDateRange(merged[^1].From, range.To > merged[^1].To ? range.To : merged[^1].To);
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }

    /// <summary>
    /// The nightly deepening of the trade history (ADR 0027): when the rebuild left holdings the trades do not explain in a way earlier history could fix (the trades explain more shares than are held, or a sale is larger than what was open), one earlier year of
    /// trades is read - the year before the earliest date actually read so far (the start of the first successful run, or what an earlier deepening reached) - and the lots are worked out again. At most one year a night; it stops for good at the first year Dhan refuses with something that trying again cannot change, at the first year that holds no
    /// trade at all, and at <see cref="IHealSettings.DeepenMaxYears"/> years back. A failure that may pass leaves the state alone and the year is tried again tomorrow. The earliest date reached is kept in the health figures and on the run. Never fails the run.
    /// </summary>
    private async ValueTask<BrokerSyncCounts> DeepenAsync(BrokerAccount account, BrokerSyncCounts counts, BrokerRebuildSummary summary, WindowWork work, CancellationToken cancellationToken)
    {
        if (healSettings.DeepenMaxYears <= 0 || !summary.NotRebuilt.Any(n => Deepenable(n)))
        {
            return counts;
        }

        var stored = await health.GetAsync(account.Id, SyncHealthKind.Trades, cancellationToken);
        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        if (stored.DeepenExhausted || stored.DeepenedOn == today)
        {
            return counts;
        }

        var limit = today.AddYears(-healSettings.DeepenMaxYears);

        // The earliest date actually read: the start of the first run that succeeded or what an earlier deepening reached, whichever is earlier - not the account's import date, which may have been moved since the first read and would leave the dates between unread.
        var firstRead = await runs.GetEarliestReadAsync(account.Id, BrokerSyncKind.Trades, cancellationToken);
        var reached = new[] { stored.DeepenedTo, firstRead }.Where(d => d is not null).Min() ?? account.Portfolio.ImportDate;
        if (reached <= limit)
        {
            await recorder.DeepenedAsync(account.Id, null, exhausted: true, today, cancellationToken);
            HealLog.DeepenStopped(logger, account.Slug, "the furthest year back is reached");
            return counts;
        }

        var from = reached.AddYears(-1);
        from = from < limit ? limit : from;
        var windows = BrokerWindows.Split(from, reached.AddDays(-1));
        counts = counts with { WindowsTotal = counts.WindowsTotal + windows.Count };
        var readBefore = counts.Read;
        foreach (var window in windows)
        {
            var next = await work(window, counts, cancellationToken);
            if (next.IsFailure)
            {
                var reason = SyncFailures.Of(next.Error);
                HealLog.DeepenFailed(logger, account.Slug, reason);

                // Dhan refusing the year for a reason trying again cannot change ends the deepening; anything that may pass is tried again tomorrow.
                if (!SyncFailures.IsTransient(reason) && !SyncFailures.IsOwnerOnly(reason) && reason != SyncFailures.Unexpected)
                {
                    await recorder.DeepenedAsync(account.Id, null, exhausted: true, today, cancellationToken);
                    HealLog.DeepenStopped(logger, account.Slug, "Dhan refused the year");
                }

                return counts;
            }

            counts = next.Value;
        }

        var empty = counts.Read == readBefore;
        var exhausted = empty || from <= limit;
        await recorder.DeepenedAsync(account.Id, from, exhausted, today, cancellationToken);
        if (exhausted)
        {
            HealLog.DeepenStopped(logger, account.Slug, empty ? "Dhan has no trades for that year" : "the furthest year back is reached");
        }

        var again = await rebuilder.RebuildAsync(account, SystemActor, cancellationToken);
        var holdings = summary.NotRebuilt.Count(n => Deepenable(n));
        HealLog.Deepened(logger, account.Slug, from, holdings);
        return again.AddTo(counts) with { Rebuilt = summary.Rebuilt + again.Rebuilt, DeepenedTo = from };
    }

    /// <summary>A holding whose trades explain more shares than are held, or sell more than was open: the part of the history that is missing could be before the dates read.</summary>
    internal static bool Deepenable(BrokerNotRebuilt held) =>
        held.Reason == RebuildStatuses.QuantityMismatch && held.Detail is MismatchReasons.NegativeOpening or MismatchReasons.SellExceedsOpen;

    /// <summary>
    /// Whether the lots are to be worked out again: when this run stored trades, when the run before did not end whole (it may have stored some that were never rebuilt), when it left holdings to retry (needs review, a quantity mismatch, a race, an
    /// ambiguous match) or when a holding appeared, came back or gained a lot since that run ended (a holdings sync or an import): trades stored while the account held no such holding (<c>no_holding</c>) are rebuilt as soon as it does, however quiet the
    /// runs in between. This is read from the holdings themselves and not from the previous run's counts, because a run that skipped the rebuild reports none and would hide what the run before it left. Otherwise nothing changed since the last rebuild and the work - a read of the
    /// holdings and of every candidate - is saved.
    /// </summary>
    private async ValueTask<bool> RebuildOwedAsync(Guid accountId, BrokerSyncRun run, BrokerSyncCounts counts, CancellationToken cancellationToken)
    {
        if (counts.Stored > 0 || counts.Enriched > 0)
        {
            return true;
        }

        var previous = await runs.GetPreviousFinishedAsync(accountId, BrokerSyncKind.Trades, run.Id, cancellationToken);
        if (previous is not { Status: SyncStatus.Succeeded, FinishedAt: { } finishedAt } || previous.Counts.NotRebuilt.Any(n => n.Reason != RebuildStatuses.NoHolding))
        {
            return true;
        }

        return await holdings.AnyAppearedSinceAsync(accountId, finishedAt + RebuiltLotsMargin, cancellationToken);
    }

    private WindowWork TradeWork(BrokerAccount account, SecretValue token, BrokerSyncRun run, string actor) => async (window, counts, cancellationToken) =>
    {
        var read = await reader.ReadTradesAsync(token, window, cancellationToken);
        return read.IsFailure ? Result.Failure<BrokerSyncCounts>(read.Error) : await StoreTradesAsync(account, run, window, read.Value, counts, actor, cancellationToken);
    };

    private WindowWork LedgerWork(BrokerAccount account, SecretValue token, BrokerSyncRun run) => async (window, counts, cancellationToken) =>
    {
        var read = await reader.ReadLedgerAsync(token, window, cancellationToken);
        return read.IsFailure ? Result.Failure<BrokerSyncCounts>(read.Error) : await StoreLedgerAsync(account, run, window, read.Value, counts, cancellationToken);
    };

    /// <summary>Stores one window of trades with the run's progress and the watermark move, in one unit of work.</summary>
    private async ValueTask<Result<BrokerSyncCounts>> StoreTradesAsync(
        BrokerAccount account, BrokerSyncRun run, DateWindow window, TradeWindowRead read, BrokerSyncCounts counts, string actor, CancellationToken cancellationToken)
    {
        var today = IndiaTime.Today(clock.GetUtcNow());
        var dropped = read.Dropped;
        var failedChecks = 0;
        var repeated = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skipped = new Dictionary<string, int>(read.Skipped, StringComparer.Ordinal);
        var usable = new List<(ResolvedBrokerTrade Row, string Id)>(read.Trades.Count);
        var resolved = await BrokerTradeSecurities.ResolveAsync(reference, read.Trades, cancellationToken);
        foreach (var trade in resolved)
        {
            if (!IsUsable(trade.Trade, today))
            {
                dropped++;
                failedChecks++;
                continue;
            }

            if (!trade.IsIdentified)
            {
                // Neither a ticker nor an ISIN, and the security id is not in the master: counted, never silently lost.
                skipped[BrokerSkipReasons.NoSymbol] = skipped.GetValueOrDefault(BrokerSkipReasons.NoSymbol) + 1;
                continue;
            }

            var id = TradeIdentity.ForTradeId(trade.Trade.Date, trade.Trade.TradeId);
            if (seen.Add(id))
            {
                usable.Add((trade, id));
            }
            else
            {
                repeated++;
            }
        }

        return await SaveWindowAsync(
            run,
            window,
            usable,
            (ids, token) => trades.StoredIdsAsync(account.Id, ids, token),
            (batch, at, _) =>
            {
                var seq = 0;
                trades.Add(
                [
                    .. batch.Select(f => Transaction.Create(
                        account.Id,
                        f.Row.InstrumentId,
                        f.Row.Isin,
                        f.Row.Symbol,
                        "NSE",
                        f.Row.Trade.Side,
                        f.Row.Trade.Date,
                        f.Row.Trade.Time,
                        f.Row.Trade.Qty,
                        f.Row.Trade.Price,
                        f.Row.Trade.Charges,
                        TradeSource.Broker,
                        null,
                        ++seq,
                        f.Id,
                        actor,
                        at,
                        run.Id)),
                ]);
                return ValueTask.FromResult(SupersedeOutcome.None);
            },
            placed => counts.AddWindow(read.Rows, placed.Stored, repeated + placed.Existing, dropped + placed.Refused, skipped, read.Halved, read.EmptyByNoData, placed.Enriched, placed.Superseded.Rows, placed.Superseded.Differing, read.Drops.Plus(BrokerDropReasons.FailedChecks, failedChecks).Plus(BrokerDropReasons.StoreRefused, placed.Refused)),
            placed => dropped + skipped.GetValueOrDefault(BrokerSkipReasons.NoSymbol) + placed.Refused,
            cancellationToken,
            EnrichAsync(account.Id),
            (stored, at, token) => SupersedeBillRowsAsync(account.Id, run.Id, stored.Select(f => f.Row).ToList(), at, token));
    }

    /// <summary>
    /// The broker's own trades replace the bill-level rows of an imported Dhan report for the same day, ISIN and side (ADR 0029). Run <b>after</b> the window's trades are saved - the whole stored group is then known, and a group of several fills is compared by its
    /// total, whichever way the window was saved (in one unit, or row by row after the database refused the whole). Inside the window's unit of work.
    /// </summary>
    private async ValueTask<SupersedeOutcome> SupersedeBillRowsAsync(Guid accountId, Guid runId, IReadOnlyList<ResolvedBrokerTrade> stored, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var keys = stored
            .Where(t => TradeRules.NormaliseIsin(t.Isin) is not null)
            .Select(t => (t.Trade.Date, Isin: TradeRules.NormaliseIsin(t.Isin)!, t.Trade.Side))
            .ToHashSet();
        if (keys.Count == 0)
        {
            return SupersedeOutcome.None;
        }

        var whole = await trades.ListBrokerGroupsAsync(accountId, keys.Min(k => k.Date), keys.Max(k => k.Date), cancellationToken);
        var groups = whole.Where(g => keys.Contains((g.Date, g.Isin, g.Side))).ToList();
        return groups.Count == 0 ? SupersedeOutcome.None : await trades.SupersedeBillRowsAsync(accountId, groups, runId, at, cancellationToken);
    }

    /// <summary>
    /// The completion of trades stored by an earlier read (ADR 0026): of the rows of a window that are stored already, those from the broker that lack an ISIN, a ticker or charges get them from this read - and nothing else changes (see
    /// <see cref="Transaction.EnrichFromBroker"/>; a trade from a file or typed in, and a voided one, are not looked at). Saved in the unit of work of the window. Returns how many trades were completed.
    /// </summary>
    private Func<IReadOnlyList<(ResolvedBrokerTrade Row, string Id)>, CancellationToken, ValueTask<int>> EnrichAsync(Guid accountId) => async (existing, cancellationToken) =>
    {
        var incomplete = await trades.ListBrokerAwaitingEnrichmentAsync(accountId, [.. existing.Select(r => r.Id)], cancellationToken);
        if (incomplete.Count == 0)
        {
            return 0;
        }

        var read = existing.ToDictionary(r => r.Id, r => r.Row, StringComparer.Ordinal);
        return incomplete.Count(t => read.TryGetValue(t.ExternalId, out var row) && t.EnrichFromBroker(row.Isin, row.Symbol, row.Trade.Charges));
    };

    /// <summary>Stores one window of the ledger with the run's progress and the watermark move, in one unit of work.</summary>
    private async ValueTask<Result<BrokerSyncCounts>> StoreLedgerAsync(
        BrokerAccount account, BrokerSyncRun run, DateWindow window, LedgerWindowRead read, BrokerSyncCounts counts, CancellationToken cancellationToken)
    {
        var identified = LedgerIdentity.Identify(account.Id, read.Rows).Select(i => (Row: i.Row, Id: i.ExternalId)).ToList();
        return await SaveWindowAsync(
            run,
            window,
            identified,
            (ids, token) => ledger.StoredIdsAsync(account.Id, ids, token),
            async (batch, at, token) =>
            {
                var entries = batch.Select(f => LedgerEntry.Create(
                    account.Id,
                    f.Row.Date,
                    f.Row.VoucherNo,
                    f.Row.Description,
                    f.Row.Narration,
                    f.Row.Exchange,
                    f.Row.Amount,
                    LedgerClassifier.Classify(f.Row.Description, f.Row.Narration, f.Row.Amount),
                    f.Row.RunningBalance,
                    f.Id,
                    run.Id,
                    at)).ToList();
                ledger.Add(entries);

                // Dhan's own charges replace the depository charges of an imported sheet for the same day and amount, so that the net figures count each once (ADR 0029).
                var charges = entries
                    .Where(e => e.EntryClass == LedgerClass.Charges && e.Amount < 0m)
                    .Select(e => new LedgerChargeOnDay(e.EntryDate, -e.Amount, DepositoryCharges.Is(e.Description, e.Narration)))
                    .ToList();
                if (charges.Count == 0)
                {
                    return SupersedeOutcome.None;
                }

                // The day is decided from the whole stored day (ADR 0030): the charges earlier saves stored for these days (the entries just added are not saved yet) decide with the new ones which charges of a day count, as the sheet's plan does.
                var storedBefore = await ledger.ListBrokerChargesAsync(account.Id, charges.Min(c => c.Date), charges.Max(c => c.Date), token);
                return new SupersedeOutcome(await ledger.SupersedeImportedChargesAsync(account.Id, charges, storedBefore, at, token), 0);
            },
            placed => counts.AddWindow(read.Received, placed.Stored, placed.Existing, read.Dropped + placed.Refused, new Dictionary<string, int>(), read.Halved, read.EmptyByNoData, superseded: placed.Superseded.Rows, drops: read.Drops.Plus(BrokerDropReasons.StoreRefused, placed.Refused)),
            placed => read.Dropped + placed.Refused,
            cancellationToken);
    }

    /// <summary>What became of the rows of a window: stored now, stored already, or refused by the database (and not stored by anyone else).</summary>
    private sealed record Placed(int Stored, int Existing, int Refused, int Enriched = 0, SupersedeOutcome? SupersededRows = null)
    {
        public SupersedeOutcome Superseded => SupersededRows ?? SupersedeOutcome.None;
    }

    private enum Fate
    {
        Stored,
        Existing,
        Refused,
    }

    /// <summary>
    /// Stores the rows of one window together with the run's progress and the watermark move, in one unit of work. When the database refuses the rows as a whole (the same trades were stored meanwhile by a tradebook import, or one row breaks a rule
    /// of the table) the window is saved <b>row by row</b> instead, each row in its own unit, and then progress and watermark in a last one: a row that turns out stored already counts as a duplicate, a row the database still refuses is dropped and
    /// counted. So one bad row never keeps a window - and with it every later one - from being read; the rows are idempotent, so a crash part-way is repaired by the next run reading the window again (the watermark moved only at the end).
    /// </summary>
    private async ValueTask<Result<BrokerSyncCounts>> SaveWindowAsync<T>(
        BrokerSyncRun run,
        DateWindow window,
        IReadOnlyList<(T Row, string Id)> rows,
        Func<IReadOnlyCollection<string>, CancellationToken, ValueTask<IReadOnlySet<string>>> storedIds,
        Func<IReadOnlyList<(T Row, string Id)>, DateTimeOffset, CancellationToken, ValueTask<SupersedeOutcome>> add,
        Func<Placed, BrokerSyncCounts> countsOf,
        Func<Placed, int> droppedOf,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<(T Row, string Id)>, CancellationToken, ValueTask<int>>? enrich = null,
        Func<IReadOnlyList<(T Row, string Id)>, DateTimeOffset, CancellationToken, ValueTask<SupersedeOutcome>>? supersedeStored = null)
    {
        BrokerSyncCounts? saved = null;
        var refused = false;
        var whole = await unitOfWork.RunAsync(
            async token =>
            {
                var at = clock.GetUtcNow();
                refused = false;
                var existing = rows.Count == 0 ? new HashSet<string>() : await storedIds([.. rows.Select(r => r.Id)], token);
                var fresh = rows.Where(r => !existing.Contains(r.Id)).ToList();
                var superseded = SupersedeOutcome.None;
                if (fresh.Count > 0)
                {
                    superseded = await add(fresh, at, token);
                }

                var enriched = 0;
                if (enrich is not null && fresh.Count < rows.Count)
                {
                    enriched = await enrich([.. rows.Where(r => existing.Contains(r.Id))], token);
                }

                if (!await unitOfWork.TrySaveAsync(token))
                {
                    refused = true;
                    return Result.Failure(PortfolioErrors.HistoryChanged);
                }

                if (supersedeStored is not null && fresh.Count > 0)
                {
                    superseded = superseded.Plus(await supersedeStored(fresh, at, token));
                }

                var placed = new Placed(fresh.Count, rows.Count - fresh.Count, 0, enriched, superseded);
                var next = countsOf(placed);

                await runs.UpdateProgressAsync(run.Id, next, token);
                await runs.MoveWatermarkAsync(run.AccountId, run.Kind, window.From, window.To, at, token);
                await health.NoteWindowAsync(run.AccountId, SyncFailures.KindOf(run.Kind), window.From, window.To, droppedOf(placed), at, token);
                saved = next;
                return Result.Success();
            },
            cancellationToken);
        if (whole.IsSuccess)
        {
            return Result.Success(saved!);
        }

        return refused ? await SaveRowByRowAsync(run, window, rows, storedIds, add, countsOf, droppedOf, enrich, supersedeStored, cancellationToken) : Result.Failure<BrokerSyncCounts>(Internal(whole.Error));
    }

    private async ValueTask<Result<BrokerSyncCounts>> SaveRowByRowAsync<T>(
        BrokerSyncRun run,
        DateWindow window,
        IReadOnlyList<(T Row, string Id)> rows,
        Func<IReadOnlyCollection<string>, CancellationToken, ValueTask<IReadOnlySet<string>>> storedIds,
        Func<IReadOnlyList<(T Row, string Id)>, DateTimeOffset, CancellationToken, ValueTask<SupersedeOutcome>> add,
        Func<Placed, BrokerSyncCounts> countsOf,
        Func<Placed, int> droppedOf,
        Func<IReadOnlyList<(T Row, string Id)>, CancellationToken, ValueTask<int>>? enrich,
        Func<IReadOnlyList<(T Row, string Id)>, DateTimeOffset, CancellationToken, ValueTask<SupersedeOutcome>>? supersedeStored,
        CancellationToken cancellationToken)
    {
        int stored = 0, existing = 0, refused = 0, enriched = 0, supersededRows = 0, supersededDiffering = 0;
        var storedRows = new List<(T Row, string Id)>();
        foreach (var row in rows)
        {
            var fate = Fate.Stored;
            var superseded = SupersedeOutcome.None;
            var unit = await unitOfWork.RunAsync(
                async token =>
                {
                    if ((await storedIds([row.Id], token)).Count > 0)
                    {
                        fate = Fate.Existing;
                        var completed = enrich is null ? 0 : await enrich([row], token);
                        if (completed > 0 && await unitOfWork.TrySaveAsync(token))
                        {
                            enriched += completed;
                        }

                        return Result.Success();
                    }

                    superseded = await add([row], clock.GetUtcNow(), token);
                    if (await unitOfWork.TrySaveAsync(token))
                    {
                        fate = Fate.Stored;
                        return Result.Success();
                    }

                    fate = Fate.Refused;
                    return Result.Failure(PortfolioErrors.HistoryChanged);
                },
                cancellationToken);
            if (unit.IsFailure && fate != Fate.Refused)
            {
                // Rolled back by a concurrency conflict (twice): the row is neither stored nor refused, so the window is not done; the next run reads it again.
                return Result.Failure<BrokerSyncCounts>(Internal(unit.Error));
            }

            if (fate == Fate.Refused && (await storedIds([row.Id], cancellationToken)).Count > 0)
            {
                fate = Fate.Existing; // someone stored it between the check and the save
            }

            if (fate == Fate.Stored)
            {
                supersededRows += superseded.Rows;
                supersededDiffering += superseded.Differing;
                storedRows.Add(row);
            }

            stored += fate == Fate.Stored ? 1 : 0;
            existing += fate == Fate.Existing ? 1 : 0;
            refused += fate == Fate.Refused ? 1 : 0;
        }

        BrokerSyncCounts? saved = null;
        var done = await unitOfWork.RunAsync(
            async token =>
            {
                var at = clock.GetUtcNow();

                // Once for the window, after every row is saved: the whole group of a day is known now, so a group of several fills is replaced once and compared by its total.
                var superseded = new SupersedeOutcome(supersededRows, supersededDiffering);
                if (supersedeStored is not null && storedRows.Count > 0)
                {
                    superseded = superseded.Plus(await supersedeStored(storedRows, at, token));
                }

                var placed = new Placed(stored, existing, refused, enriched, superseded);
                var next = countsOf(placed);
                await runs.UpdateProgressAsync(run.Id, next, token);
                await runs.MoveWatermarkAsync(run.AccountId, run.Kind, window.From, window.To, at, token);
                await health.NoteWindowAsync(run.AccountId, SyncFailures.KindOf(run.Kind), window.From, window.To, droppedOf(placed), at, token);
                saved = next;
                return Result.Success();
            },
            cancellationToken);
        return done.IsSuccess ? Result.Success(saved!) : Result.Failure<BrokerSyncCounts>(Internal(done.Error));
    }

    /// <summary>
    /// Ends the run as succeeded, with its audit event (counts only), in one unit of work. A scheduled run leaves an audit event only when it stored something, rebuilt a holding, or changed which holdings need the owner's review: the same hand-dated
    /// holding is not news every evening.
    /// </summary>
    private async ValueTask<Result> FinishAsync(BrokerAccount account, BrokerSyncRun run, BrokerSyncCounts counts, bool scheduled, string actor, CancellationToken cancellationToken)
    {
        var kind = BrokerHistoryViews.Name(run.Kind);
        var audit = !scheduled || await IsNewsAsync(account.Id, run, counts, cancellationToken);
        var finished = await unitOfWork.RunAsync(
            async token =>
            {
                var now = clock.GetUtcNow();
                if (audit)
                {
                    events.Record(AccountEvent.Record(AccountEventType.BrokerHistorySynced, account.Id, Describe(run, counts), actor, now));
                    await unitOfWork.SaveAsync(token);
                }

                await runs.CompleteAsync(run.Id, SyncStatus.Succeeded, counts, null, now, token);
                return Result.Success();
            },
            cancellationToken);
        if (finished.IsFailure)
        {
            // Nothing was saved (a concurrency conflict, twice): the run is not complete, so it is ended as failed by the caller and the next run reads from the watermark.
            return Result.Failure(Internal(finished.Error));
        }

        var droppedReasons = Reasons(counts);
        BrokerHistoryLog.Succeeded(logger, kind, run.Id, counts.Stored, counts.Duplicates, counts.Dropped, droppedReasons, counts.Skipped, counts.WindowsDone, counts.Halved);
        return Result.Success();
    }

    /// <summary>The error of a failed unit of work as the run reports it: the manual flows' "try again" (<see cref="PortfolioErrors.DataChanged"/>) means nothing to a run, whose next attempt reads the window again.</summary>
    private static Error Internal(Error error) => error == PortfolioErrors.DataChanged ? PortfolioErrors.HoldingChanged : error;

    /// <summary>True when a run changed something: it stored rows or rebuilt holdings, or (trades) the holdings that need review are not the ones the last successful run reported.</summary>
    private async ValueTask<bool> IsNewsAsync(Guid accountId, BrokerSyncRun run, BrokerSyncCounts counts, CancellationToken cancellationToken)
    {
        if (counts.Stored > 0 || counts.Rebuilt > 0 || counts.Enriched > 0)
        {
            return true;
        }

        if (run.Kind != BrokerSyncKind.Trades)
        {
            return false;
        }

        var before = (await runs.GetLastSucceededAsync(accountId, BrokerSyncKind.Trades, cancellationToken))?.Counts;
        return counts.NeedsReview != (before?.NeedsReview ?? 0) || !ReviewSymbols(counts).SequenceEqual(before is null ? [] : ReviewSymbols(before), StringComparer.Ordinal);
    }

    private static IEnumerable<string> ReviewSymbols(BrokerSyncCounts counts) =>
        counts.NotRebuilt.Where(n => n.Reason == BrokerLotRebuilder.NeedsReview).Select(n => n.Symbol).Order(StringComparer.Ordinal);

    internal static string Describe(BrokerSyncRun run, BrokerSyncCounts counts)
    {
        var trigger = BrokerHistoryViews.Name(run.Trigger);
        var halved = counts.Halved > 0 ? $", {counts.Halved} halved" : string.Empty;
        var noData = counts.EmptyByNoData > 0 ? $", {counts.EmptyByNoData} taken as empty on Dhan's no-data code" : string.Empty;
        var completed = counts.Enriched > 0 ? $" ({counts.Enriched} completed with an ISIN, charges or ticker)" : string.Empty;
        var deepened = counts.DeepenedTo is { } earliest ? $"; earlier history read back to {earliest:yyyy-MM-dd}" : string.Empty;
        var superseded = counts.Superseded > 0 ? $"; {counts.Superseded} bill-level rows of imported reports replaced ({counts.SupersededDiffering} groups with a different total)" : string.Empty;
        var replaced = counts.Superseded > 0 ? $"; {counts.Superseded} imported depository charges replaced" : string.Empty;
        return run.Kind == BrokerSyncKind.Trades
            ? $"Broker trades read ({trigger}): {counts.WindowsDone} windows{halved}{noData}, {counts.Stored} new trades, {counts.Duplicates} already stored{completed}, {counts.Skipped} skipped, {counts.Dropped} dropped{Reasons(counts)}; {counts.Rebuilt} holdings rebuilt, {counts.NeedsReview} need review{deepened}{superseded}"
            : $"Broker ledger read ({trigger}): {counts.WindowsDone} windows{halved}{noData}, {counts.Stored} new entries, {counts.Duplicates} already stored, {counts.Dropped} dropped{Reasons(counts)}{replaced}";
    }

    /// <summary>The most drop reasons spelled out in the audit and log text; the rest are summed up as <c>+N more</c> so that the figures after them are not cut at <see cref="AccountEvent.MaxDetailLength"/>. The run counts keep every reason.</summary>
    internal const int MaxReasonsListed = 3;

    /// <summary>
    /// The reasons of the dropped rows as <c> (reason×count, ...)</c>, the biggest first and at most <see cref="MaxReasonsListed"/> of them, then <c>+N more</c> for the others; empty when none. Counts only: nothing of a row, not even its place in the answer.
    /// </summary>
    internal static string Reasons(BrokerSyncCounts counts)
    {
        if (counts.DroppedByReason.Count == 0)
        {
            return string.Empty;
        }

        var ordered = counts.DroppedByReason.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        var listed = ordered.Take(MaxReasonsListed).Select(p => $"{p.Key}×{p.Value}").ToList();
        if (ordered.Count > MaxReasonsListed)
        {
            listed.Add($"+{ordered.Count - MaxReasonsListed} more");
        }

        return " (" + string.Join(", ", listed) + ")";
    }

    private async ValueTask<bool> FailAsync(BrokerAccount account, BrokerSyncRun run, BrokerSyncCounts counts, Error error)
    {
        var reason = SyncFailures.Of(error);
        BrokerHistoryLog.Failed(logger, BrokerHistoryViews.Name(run.Kind), run.Id, error.Code);
        await runs.CompleteAsync(run.Id, SyncStatus.Failed, counts with { FailureReason = reason }, error.Message, clock.GetUtcNow(), CancellationToken.None);
        await recorder.FailedAsync(account.Id, SyncFailures.KindOf(run.Kind), reason, CancellationToken.None);
        return false;
    }

    /// <summary>A trade the store would refuse (or that cannot be dated) is dropped and counted instead of failing the window.</summary>
    private static bool IsUsable(BrokerTrade trade, DateOnly today) =>
        trade.TradeId.Length is > 0 and <= TradeCsvLimits.MaxTradeIdLength
        && TradeRules.QtyProblem(trade.Qty) is null
        && TradeRules.PriceProblem(TradeRules.Round(trade.Price)) is null
        && TradeRules.ChargesProblem(TradeRules.Round(trade.Charges)) is null
        && trade.Date >= TradeRules.EarliestDate
        && trade.Date <= today;
}
