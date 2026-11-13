using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Replay.Job;

/// <summary>
/// Works the queued system replays (spec SR-01 part B, ADR 0272), in the pattern of <c>SimAllRunner</c>: the table is the queue and the memory. It claims one run, steps its sessions one at a time in order through the pure session step of part A, stores each session with the
/// trades that closed by it and moves the cursor in one transaction (so the step is idempotent by run and session), and ends the run as <c>completed</c>, <c>failed</c> (the reason of the session that failed; nothing is skipped) or <c>cancelled</c> (a cancel is seen after the current session).
/// <para>
/// <b>Resume.</b> The book lives in memory, so a run that restarts replays its stored sessions again from the start without writing them and checks that each gives the hash that is stored; then it goes on at the next session. The records are therefore byte-identical, and a record that is not
/// (a changed bar or a defect) fails the run with <c>replay.diverged</c> instead of being mixed in. The settings snapshot is the one stored with the run, never today's.
/// </para>
/// <para>
/// <b>Yield.</b> The same rules as <c>SimAllRunner.YieldAsync</c> (low disk, market hours, a bar-history job or a recovery pass, an incomplete history backfill), and it does not run while a simulate-everything chunk runs. A session is never cut short: the rules are looked at before each new session.
/// The job reads stored bars and writes only the three replay tables and one trial-ledger row at the start; it places and references no order.
/// </para>
/// </summary>
public sealed class ReplayRunner(IServiceScopeFactory scopes, SystemReplayOptions options, TimeProvider time, ILogger<ReplayRunner> logger, HoldOut.HoldOutRunActivity? holdOut = null)
{
    /// <summary>The recovery class of the job (AU-01, ADR 0240): it is convergent, since running it again closes whatever a stop left open (a run goes on from its cursor). Pinned in <c>RecoveryClassTableTests</c>.</summary>
    public const RecoveryClass Recovery = RecoveryClass.Convergent;

    /// <summary>The reason given while a simulate-everything chunk runs.</summary>
    public const string SimAllRunning = "sim_all";

    /// <summary>How many sessions pass between two progress log lines.</summary>
    private const int ProgressEvery = 50;

    /// <summary>After this many consecutive failures of the store (or of the preparation), each after a longer wait (see <see cref="MaxBackoff"/>: about ten minutes in all), the run ends failed with the reason instead of being tried for ever.</summary>
    private const int MaxConsecutiveFailures = 8;

    /// <summary>The wait after a failure of the store doubles from the poll interval up to this, so a database outage of some minutes does not fail a run for good.</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>No run starts a session within this time of the market window opening (the session is short, the margin is as for the simulate-everything chunk).</summary>
    private static readonly TimeSpan WindowMargin = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan BackfillEvery = TimeSpan.FromMinutes(1);

    private DateTimeOffset? _backfillCheckedAt;
    private bool _backfillIncomplete;
    private string? _lastYield;
    private volatile ActiveRun? _active;
    private bool _recovered;

    /// <summary>Whether a run is in hand now (SR-03: the hold-out replay waits while it is, so two heavy replays never share the memory budget).</summary>
    public bool Busy => _active is not null;

    /// <summary>The loop of the host: recover once, then look every poll interval until the host stops.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await GuardedAsync(TickAsync, stoppingToken);
                await Task.Delay(options.PollInterval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down: a run in flight stays running and the next start takes it back
        }
        finally
        {
            _active?.Scope.Dispose();
            _active = null;
        }
    }

    /// <summary>After a stop: a run left running goes back to queued (it resumes at the next session), a run left cancelling is cancelled. Call once before the first tick.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IReplayStore>();
        var touched = await store.RecoverAsync(time.GetUtcNow(), cancellationToken);
        _recovered = true; // only after it worked: a failed recovery is tried again at the next tick, so a stale running row cannot block the claims until a restart
        if (touched > 0)
        {
            ReplayLog.Recovered(logger, touched);
        }
    }

    /// <summary>One look at the queue: go on with the run in hand, or claim the oldest queued one, and work it until it ends, the yield rules say wait or the host stops.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!_recovered)
        {
            await RecoverAsync(cancellationToken);
        }

        if (_active is null)
        {
            if (holdOut is { Running: true })
            {
                return; // a hold-out system replay is running (SR-03): two heavy replays never share the memory budget
            }

            var scope = scopes.CreateAsyncScope();
            if (await YieldAsync(scope.ServiceProvider, true, cancellationToken) is { } reason)
            {
                await scope.DisposeAsync();
                NoteYield(null, reason);
                return;
            }

            var store = scope.ServiceProvider.GetRequiredService<IReplayStore>();
            var claimed = await store.ClaimAsync(cancellationToken);
            if (claimed is null)
            {
                await scope.DisposeAsync();
                return;
            }

            _active = new ActiveRun(scope, claimed);
            if (claimed.CursorSession is not null)
            {
                ReplayLog.Resume(logger, claimed.Id, claimed.CursorSession);
            }
        }

        var run = _active;
        if (run.NotBefore > time.GetUtcNow())
        {
            // backing off after a failure of the store; a cancel is not made to wait for it
            var waiting = run.Scope.ServiceProvider.GetRequiredService<IReplayStore>();
            if (await waiting.StateAsync(run.Row.Id, cancellationToken) == ReplayRunStates.Cancelling && await EndAsync(run, waiting, ReplayRunStates.Cancelled, null, null, cancellationToken))
            {
                run.Scope.Dispose();
                _active = null;
            }

            return;
        }

        var ended = false;
        try
        {
            ended = await WorkAsync(run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the host stops: the run stays running and the next start takes it back
        }
#pragma warning disable CA1031 // a failed read or write of the store must not leave a book that has stepped past what is stored: the run is rebuilt from its stored sessions at the next tick, and ends failed after repeated failures
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ReplayLog.SessionCrashed(logger, run.Row.Id, ex);
            run.Reset();
            run.NotBefore = time.GetUtcNow() + TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, options.PollInterval.Ticks << Math.Min(run.Failures, 20)));
            if (++run.Failures >= MaxConsecutiveFailures)
            {
                ended = await EndAsync(run, run.Scope.ServiceProvider.GetRequiredService<IReplayStore>(), ReplayRunStates.Failed, null, "replay.store_failed: " + ex.GetType().Name + ": " + ex.Message, cancellationToken);
            }
        }
        finally
        {
            if (ended)
            {
                run.Scope.Dispose();
                _active = null;
            }
        }
    }

    /// <summary>Steps the run until it ends (true) or has to wait (false).</summary>
    private async Task<bool> WorkAsync(ActiveRun run, CancellationToken cancellationToken)
    {
        var services = run.Scope.ServiceProvider;
        var store = services.GetRequiredService<IReplayStore>();
        var id = run.Row.Id;
        if (run.Prepared is null)
        {
            var parameters = ReplayJson.Read<ReplayParams>(run.Row.ParamsJson);
            var settings = ReplayJson.Read<ReplaySettingsSnapshot>(run.Row.SettingsJson);
            var stored = ReplayJson.Read<Dictionary<string, List<StoredVersion>>>(run.Row.VersionsJson);
            if (parameters is null || settings is null || stored is null)
            {
                return await FailAsync(run, store, null, "replay.unreadable", "The stored parameters, settings snapshot or strategy versions cannot be read.", cancellationToken);
            }

            var versions = stored.ToDictionary(p => p.Key, p => (IReadOnlyList<StoredVersion>)p.Value, StringComparer.Ordinal);
            var hasher = services.GetRequiredService<ReplayInputHasher>();
            var prepared = await services.GetRequiredService<ReplayDriver>().PrepareAsync(parameters, settings, versions, cancellationToken, run.Row.LearningSources, parameters.Mode == ReplayMode.Learning);
            if (prepared.IsFailure)
            {
                return await FailAsync(run, store, null, prepared.Error.Code, prepared.Error.Message, cancellationToken);
            }

            var (input, _, _) = await hasher.ComputeAsync(parameters, settings, versions, prepared.Value.Sessions, run.Row.LearningSources, cancellationToken);
            if (input != run.Row.InputHash)
            {
                return await FailAsync(run, store, null, "replay.diverged", "The inputs (bars, members, statistics, versions or settings) have changed since the run was started, so it cannot go on.", cancellationToken);
            }

            run.Prepared = prepared.Value;
            run.Stored = new Dictionary<DateOnly, string>(await store.SessionHashesAsync(id, cancellationToken));
            run.Cursor = run.Stored.Count == 0 ? null : run.Stored.Keys.Max();
        }

        var prep = run.Prepared;
        var driver = services.GetRequiredService<ReplayDriver>();
        while (run.Next < prep.Sessions.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = prep.Sessions[run.Next];
            var replayed = run.Cursor is { } cursor && session <= cursor; // stored already: replayed again only to rebuild the book and to check the hash
            if (await store.StateAsync(id, cancellationToken) == ReplayRunStates.Cancelling)
            {
                return await EndAsync(run, store, ReplayRunStates.Cancelled, null, null, cancellationToken); // also while the stored sessions are replayed: a cancel is not made to wait for the rebuild
            }

            if (await YieldAsync(services, true, cancellationToken) is { } reason)
            {
                NoteYield(id, reason);
                return false;
            }

            _lastYield = null;

            Result<ReplaySessionRecord> stepped;
            try
            {
                stepped = await driver.StepAsync(prep, session, cancellationToken);
            }
            catch (ReplayHoldOutException ex)
            {
                return await FailAsync(run, store, session, ReplayDriver.HoldOutCode, ex.Message, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return await FailAsync(run, store, session, "replay.cancelled_inside", "A read inside the session was cancelled.", cancellationToken);
            }
#pragma warning disable CA1031 // any other failure of a session stops this run with its reason and is logged; the loop of the host goes on
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                ReplayLog.SessionCrashed(logger, id, ex);
                return await FailAsync(run, store, session, "replay.session_crashed", ex.GetType().Name + ": " + ex.Message, cancellationToken);
            }

            if (stepped.IsFailure)
            {
                return await FailAsync(run, store, session, stepped.Error.Code, stepped.Error.Message, cancellationToken);
            }

            var record = stepped.Value;
            var text = ReplayJson.Canonical(record);
            var hash = ReplayJson.Sha256(text);
            run.Booked = prep.Book.Trades.Count;
            var closed = ClosedBy(prep, session, run.StoredTrades);
            if (replayed)
            {
                if (!run.Stored.TryGetValue(session, out var was) || was != hash)
                {
                    return await FailAsync(run, store, session, "replay.diverged", "The session gives another record than the one stored; the run cannot go on from it.", cancellationToken);
                }
            }
            else
            {
                if (!await store.SaveSessionAsync(id, new ReplaySessionRow(session, text, record.Equity, record.DrawdownPct, hash), closed, cancellationToken))
                {
                    return await FailAsync(run, store, session, "replay.diverged", "The session could not be stored as new: it is stored already or the run is no longer active.", cancellationToken);
                }

                run.Failures = 0;
                run.NotBefore = null;
                run.Stored[session] = hash;
                run.Cursor = session;
                if (run.Stored.Count % ProgressEvery == 0)
                {
                    ReplayLog.Progress(logger, id, run.Stored.Count, prep.Sessions.Count, session);
                }
            }

            run.Next++;
        }

        var result = ReplayHashes.Result(prep.Sessions.Select(s => run.Stored[s]));
        return await EndAsync(run, store, ReplayRunStates.Completed, result, null, cancellationToken);
    }

    private async Task<bool> FailAsync(ActiveRun run, IReplayStore store, DateOnly? session, string code, string message, CancellationToken cancellationToken)
    {
        var reason = (session is { } day ? "session " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " : string.Empty) + code + ": " + message;
        ReplayLog.Failed(logger, run.Row.Id, code);
        return await EndAsync(run, store, ReplayRunStates.Failed, null, reason, cancellationToken);
    }

    private async Task<bool> EndAsync(ActiveRun run, IReplayStore store, string state, string? resultHash, string? failReason, CancellationToken cancellationToken)
    {
        var rows = run.Prepared is null ? [] : OpenAndUnstored(run.Prepared, run.Booked, run.Cursor, run.StoredTrades);
        await store.FinishAsync(run.Row.Id, state, resultHash, failReason, rows, time.GetUtcNow(), cancellationToken);
        switch (state)
        {
            case ReplayRunStates.Completed:
                ReplayLog.Finished(logger, run.Row.Id, run.Stored.Count);
                break;
            case ReplayRunStates.Cancelled:
                ReplayLog.Cancelled(logger, run.Row.Id, run.Stored.Count);
                break;
        }

        return true;
    }

    /// <summary>The trades of the book that closed on or before <paramref name="session"/> and are not stored yet; they are marked stored.</summary>
    private static List<ReplayTradeRow> ClosedBy(ReplayPrepared prepared, DateOnly session, HashSet<long> stored)
    {
        var rows = new List<ReplayTradeRow>();
        foreach (var t in prepared.Book.Trades.Where(t => t.ExitSession <= session && !stored.Contains(t.Id)))
        {
            stored.Add(t.Id);
            rows.Add(Row(t, session));
        }

        return rows;
    }

    /// <summary>At the end of a run: every trade of the sessions that finished and is not stored yet, with its exit only when that is on or before the cursor (an open trade is stored as open).</summary>
    private static List<ReplayTradeRow> OpenAndUnstored(ReplayPrepared prepared, int booked, DateOnly? cursor, HashSet<long> stored) =>
        cursor is not { } last ? [] : [.. prepared.Book.Trades.Take(booked).Where(t => !stored.Contains(t.Id)).Select(t => Row(t, last))];

    private static ReplayTradeRow Row(ReplayTrade t, DateOnly asOf)
    {
        var exit = t.Exit is { } e && t.ExitSession <= asOf ? e : null;
        return new ReplayTradeRow(
            t.Id, t.Item.Symbol, t.Item.Side, t.Item.Evidence.CellKey, t.Qty, t.EntrySession, t.EntryAt, t.EntryPrice, t.StopPrice, t.Swing, t.TradeKey,
            exit is null ? null : t.ExitSession, exit?.ExitAt, exit?.GrossPnl, exit?.Charges, exit?.NetPnl, exit?.ChargeSheetId, exit?.RSim);
    }

    /// <summary>Why the job should wait now, or null. The rules of <c>SimAllRunner.YieldAsync</c>, and a simulate-everything chunk in flight. Cheap checks first; the backfill is asked once a minute.</summary>
    private async Task<string?> YieldAsync(IServiceProvider services, bool includeBackfill, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (services.GetRequiredService<IFreeDiskProbe>().FreeBytes() is not { } free || free < options.MinFreeBytes)
        {
            return SimAllPause.LowDisk;
        }

        if (!options.RunInMarketHours)
        {
            var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            if (SimAllPause.InMarketHours(calendar, now) || (SimAllPause.NextWindowStart(calendar, now) is { } next && next - now < WindowMargin))
            {
                return SimAllPause.MarketHours;
            }
        }

        var gate = services.GetRequiredService<ISimAllBackfillGate>();
        if (await gate.DataJobRunningAsync(cancellationToken))
        {
            return SimAllPause.DataJob; // a bar-history job or a recovery pass
        }

        if (await services.GetRequiredService<ISimAllStore>().GetActiveAsync(cancellationToken) is { State: SimAllStates.Running })
        {
            return SimAllRunning;
        }

        if (includeBackfill)
        {
            if (_backfillCheckedAt is not { } checkedAt || now - checkedAt >= BackfillEvery)
            {
                _backfillIncomplete = await gate.IncompleteReasonAsync(cancellationToken) is not null;
                _backfillCheckedAt = now;
            }

            if (_backfillIncomplete)
            {
                return SimAllPause.Backfill;
            }
        }

        return null;
    }

    private void NoteYield(Guid? runId, string reason)
    {
        if (_lastYield != reason)
        {
            _lastYield = reason;
            ReplayLog.Yield(logger, runId ?? Guid.Empty, reason);
        }
    }

    private async Task GuardedAsync(Func<CancellationToken, Task> step, CancellationToken stoppingToken)
    {
        try
        {
            await step(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the loop of the host must survive a failed step
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ReplayLog.LoopFailed(logger, ex);
        }
    }

    /// <summary>The run in hand: it stays in memory across ticks, so a yield does not make it replay its stored sessions again.</summary>
    private sealed class ActiveRun(IServiceScope scope, ReplayRunRow row)
    {
        public IServiceScope Scope { get; } = scope;

        public ReplayRunRow Row { get; } = row;

        public ReplayPrepared? Prepared { get; set; }

        public Dictionary<DateOnly, string> Stored { get; set; } = [];

        public HashSet<long> StoredTrades { get; } = [];

        public DateOnly? Cursor { get; set; }

        public int Next { get; set; }

        public int Booked { get; set; }

        public int Failures { get; set; }

        public DateTimeOffset? NotBefore { get; set; }

        /// <summary>Forgets what was built in memory, so the next tick rebuilds the book from the stored sessions and checks their hashes.</summary>
        public void Reset()
        {
            Prepared = null;
            Next = 0;
            Booked = 0;
            Cursor = null;
            Stored = [];
            StoredTrades.Clear();
        }
    }
}
