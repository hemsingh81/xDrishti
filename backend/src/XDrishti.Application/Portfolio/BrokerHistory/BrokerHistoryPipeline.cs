using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
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
        var run = BrokerSyncRun.Start(account.Id, kind, trigger, from, IndiaTime.Today(now), actor, now);
        return await runs.TryStartAsync(run, StaleAfter, now, cancellationToken) ? Result.Success(run) : Result.Failure<BrokerSyncRun>(PortfolioErrors.SyncRunning);
    }

    /// <summary>Records that a scheduled run was not attempted (the token has expired).</summary>
    public async ValueTask RecordSkippedAsync(BrokerAccount account, string reason, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var now = clock.GetUtcNow();
        await runs.RecordSkippedAsync(BrokerSyncRun.Skipped(account.Id, BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, IndiaTime.Today(now), actor, reason, now), cancellationToken);
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
            await EndAsync(tradesRun.Id, Interrupted);
            if (ledgerRun is not null)
            {
                await EndAsync(ledgerRun.Id, Interrupted);
            }

            throw;
        }
#pragma warning disable CA1031 // any failure must end the run records as failed, never leave them running
        catch (Exception ex)
#pragma warning restore CA1031
        {
            BrokerHistoryLog.Crashed(logger, BrokerHistoryViews.Name(tradesRun.Kind), tradesRun.Id, ex);
            await EndAsync(tradesRun.Id, PortfolioErrors.SyncFailed.Message);
            if (ledgerRun is not null)
            {
                await EndAsync(ledgerRun.Id, PortfolioErrors.SyncFailed.Message);
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
        await EndAsync(runId, PortfolioErrors.SyncFailed.Message);
    }

    /// <summary>Ends a run that is still running because the application is stopping: failed, "interrupted"; the next run reads from the watermark. Never throws.</summary>
    public ValueTask InterruptedAsync(Guid runId) => EndAsync(runId, Interrupted);

    /// <summary>Ends a run that is still running with a plain reason (an account or token that cannot be used any more). Never throws.</summary>
    public async ValueTask EndAsync(Guid runId, string reason)
    {
        try
        {
            // The token that cannot be cancelled: this must work while the application is stopping. The counts so far stay.
            var current = await runs.GetAsync(runId, CancellationToken.None);
            if (current is { IsRunning: true })
            {
                await runs.CompleteAsync(runId, SyncStatus.Failed, current.Counts, reason, clock.GetUtcNow(), CancellationToken.None);
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
        var counts = BrokerSyncCounts.None;
        try
        {
            var windows = BrokerWindows.Split(run.FromDate, run.ToDate);
            counts = counts with { WindowsTotal = windows.Count };
            var trigger = BrokerHistoryViews.Name(run.Trigger);
            BrokerHistoryLog.Started(logger, kind, run.Id, account.Slug, trigger, windows.Count);
            await runs.UpdateProgressAsync(run.Id, counts, cancellationToken);
            foreach (var window in windows)
            {
                var next = await work(window, counts, cancellationToken);
                if (next.IsFailure)
                {
                    return await FailAsync(run, counts, next.Error);
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
                counts = (await rebuilder.RebuildAsync(account, SystemActor, cancellationToken)).AddTo(counts);
            }

            await FinishAsync(account, run, counts, scheduled, actor, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await runs.CompleteAsync(run.Id, SyncStatus.Failed, counts, Interrupted, clock.GetUtcNow(), CancellationToken.None);
            throw;
        }
#pragma warning disable CA1031 // any failure must end the run record as failed, never leave it running
        catch (Exception ex)
#pragma warning restore CA1031
        {
            BrokerHistoryLog.Crashed(logger, kind, run.Id, ex);
            return await FailAsync(run, counts, PortfolioErrors.SyncFailed);
        }
    }

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
                return ValueTask.CompletedTask;
            },
            placed => counts.AddWindow(read.Rows, placed.Stored, repeated + placed.Existing, dropped + placed.Refused, skipped, read.Halved, read.EmptyByNoData, placed.Enriched),
            cancellationToken,
            EnrichAsync(account.Id));
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
            (batch, at, _) =>
            {
                ledger.Add(
                [
                    .. batch.Select(f => LedgerEntry.Create(
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
                        at)),
                ]);
                return ValueTask.CompletedTask;
            },
            placed => counts.AddWindow(read.Received, placed.Stored, placed.Existing, read.Dropped + placed.Refused, new Dictionary<string, int>(), read.Halved, read.EmptyByNoData),
            cancellationToken);
    }

    /// <summary>What became of the rows of a window: stored now, stored already, or refused by the database (and not stored by anyone else).</summary>
    private sealed record Placed(int Stored, int Existing, int Refused, int Enriched = 0);

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
        Func<IReadOnlyList<(T Row, string Id)>, DateTimeOffset, CancellationToken, ValueTask> add,
        Func<Placed, BrokerSyncCounts> countsOf,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<(T Row, string Id)>, CancellationToken, ValueTask<int>>? enrich = null)
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
                if (fresh.Count > 0)
                {
                    await add(fresh, at, token);
                }

                var enriched = 0;
                if (enrich is not null && fresh.Count < rows.Count)
                {
                    enriched = await enrich([.. rows.Where(r => existing.Contains(r.Id))], token);
                }

                var next = countsOf(new Placed(fresh.Count, rows.Count - fresh.Count, 0, enriched));
                if (!await unitOfWork.TrySaveAsync(token))
                {
                    refused = true;
                    return Result.Failure(PortfolioErrors.HistoryChanged);
                }

                await runs.UpdateProgressAsync(run.Id, next, token);
                await runs.MoveWatermarkAsync(run.AccountId, run.Kind, window.From, window.To, at, token);
                saved = next;
                return Result.Success();
            },
            cancellationToken);
        if (whole.IsSuccess)
        {
            return Result.Success(saved!);
        }

        return refused ? await SaveRowByRowAsync(run, window, rows, storedIds, add, countsOf, enrich, cancellationToken) : Result.Failure<BrokerSyncCounts>(whole.Error);
    }

    private async ValueTask<Result<BrokerSyncCounts>> SaveRowByRowAsync<T>(
        BrokerSyncRun run,
        DateWindow window,
        IReadOnlyList<(T Row, string Id)> rows,
        Func<IReadOnlyCollection<string>, CancellationToken, ValueTask<IReadOnlySet<string>>> storedIds,
        Func<IReadOnlyList<(T Row, string Id)>, DateTimeOffset, CancellationToken, ValueTask> add,
        Func<Placed, BrokerSyncCounts> countsOf,
        Func<IReadOnlyList<(T Row, string Id)>, CancellationToken, ValueTask<int>>? enrich,
        CancellationToken cancellationToken)
    {
        int stored = 0, existing = 0, refused = 0, enriched = 0;
        foreach (var row in rows)
        {
            var fate = Fate.Stored;
            _ = await unitOfWork.RunAsync(
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

                    await add([row], clock.GetUtcNow(), token);
                    if (await unitOfWork.TrySaveAsync(token))
                    {
                        fate = Fate.Stored;
                        return Result.Success();
                    }

                    fate = Fate.Refused;
                    return Result.Failure(PortfolioErrors.HistoryChanged);
                },
                cancellationToken);
            if (fate == Fate.Refused && (await storedIds([row.Id], cancellationToken)).Count > 0)
            {
                fate = Fate.Existing; // someone stored it between the check and the save
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
                var next = countsOf(new Placed(stored, existing, refused, enriched));
                await runs.UpdateProgressAsync(run.Id, next, token);
                await runs.MoveWatermarkAsync(run.AccountId, run.Kind, window.From, window.To, at, token);
                saved = next;
                return Result.Success();
            },
            cancellationToken);
        return done.IsSuccess ? Result.Success(saved!) : Result.Failure<BrokerSyncCounts>(done.Error);
    }

    /// <summary>
    /// Ends the run as succeeded, with its audit event (counts only), in one unit of work. A scheduled run leaves an audit event only when it stored something, rebuilt a holding, or changed which holdings need the owner's review: the same hand-dated
    /// holding is not news every evening.
    /// </summary>
    private async ValueTask FinishAsync(BrokerAccount account, BrokerSyncRun run, BrokerSyncCounts counts, bool scheduled, string actor, CancellationToken cancellationToken)
    {
        var kind = BrokerHistoryViews.Name(run.Kind);
        var audit = !scheduled || await IsNewsAsync(account.Id, run, counts, cancellationToken);
        _ = await unitOfWork.RunAsync(
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
        BrokerHistoryLog.Succeeded(logger, kind, run.Id, counts.Stored, counts.Duplicates, counts.Dropped, counts.Skipped, counts.WindowsDone, counts.Halved);
    }

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

    private static string Describe(BrokerSyncRun run, BrokerSyncCounts counts)
    {
        var trigger = BrokerHistoryViews.Name(run.Trigger);
        var halved = counts.Halved > 0 ? $", {counts.Halved} halved" : string.Empty;
        var noData = counts.EmptyByNoData > 0 ? $", {counts.EmptyByNoData} taken as empty on Dhan's no-data code" : string.Empty;
        var completed = counts.Enriched > 0 ? $" ({counts.Enriched} completed with an ISIN, charges or ticker)" : string.Empty;
        return run.Kind == BrokerSyncKind.Trades
            ? $"Broker trades read ({trigger}): {counts.WindowsDone} windows{halved}{noData}, {counts.Stored} new trades, {counts.Duplicates} already stored{completed}, {counts.Skipped} skipped, {counts.Dropped} unusable; {counts.Rebuilt} holdings rebuilt, {counts.NeedsReview} need review"
            : $"Broker ledger read ({trigger}): {counts.WindowsDone} windows{halved}{noData}, {counts.Stored} new entries, {counts.Duplicates} already stored, {counts.Dropped} unusable";
    }

    private async ValueTask<bool> FailAsync(BrokerSyncRun run, BrokerSyncCounts counts, Error error)
    {
        BrokerHistoryLog.Failed(logger, BrokerHistoryViews.Name(run.Kind), run.Id, error.Code);
        await runs.CompleteAsync(run.Id, SyncStatus.Failed, counts, error.Message, clock.GetUtcNow(), CancellationToken.None);
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
