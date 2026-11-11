using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Replay;

namespace XDrishti.Api.IntegrationTests.Replay;

/// <summary>
/// SR-01 part B over a real TimescaleDB: the replay store keeps one active replay, claims a run only when none runs, stores a session with its trades and the cursor in one step (idempotent by run and session), puts a run left running back in the queue with its cursor, cancels a queued
/// or a running run, ends a run only through the legal states, keeps a finished run as it was (a trigger), reads the run with its completeness and trial number, and the trial ledger accepts the kind <c>system_replay</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReplayStoreTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D1 = new(2026, 10, 5);
    private static readonly DateOnly D2 = new(2026, 10, 6);
    private static readonly DateOnly D3 = new(2026, 10, 7);
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 14, 30, 0, TimeSpan.Zero);

    private ScratchDatabase? _db;

    private ScratchDatabase Db => _db!;

    public async ValueTask InitializeAsync() => _db = await ScratchDatabase.CreateAsync(factory, "xd_replay_" + Guid.NewGuid().ToString("N")[..8], CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }

    private async Task<T> WithAsync<T>(Func<IReplayStore, IReplayReads, Task<T>> action)
    {
        await using var scope = Db.Host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IReplayStore>(), scope.ServiceProvider.GetRequiredService<IReplayReads>());
    }

    private static NewReplayRun New(Guid? id = null, DateOnly? to = null, string mode = "frozen", int minutes = 0) => new(
        id ?? Guid.NewGuid(), D1, to ?? D3, mode, """{"capital":1000000,"mode":"Frozen"}""", """{"fill":{}}""", """{"S1":[]}""", null, new string('a', 64), new string('b', 64), 3, T0.AddMinutes(minutes));

    private static ReplaySessionRow Session(DateOnly day, decimal equity = 1_000_000m) => new(day, "{\"session\":\"" + day.ToString("yyyy-MM-dd") + "\"}", equity, 0.5m, new string('c', 64));

    private static ReplayTradeRow Trade(long no, bool closed, decimal? net = 120m) => new(
        no, "AAA", "BUY", "S1|1D|BUY|swing|FIXED_RR", 10, D1, new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.FromHours(5.5)), 100m, 98m, true, "S1|AAA|BUY|2026-10-05",
        closed ? D2 : null, closed ? new DateTimeOffset(2026, 10, 6, 9, 30, 0, TimeSpan.FromHours(5.5)) : null, closed ? 120m : null, closed ? 5m : null, closed ? net : null, closed ? "sheet" : null, closed ? 1.2 : null);

    private async Task<Guid> QueueAsync(string mode = "frozen", int minutes = 0)
    {
        var run = New(mode: mode, minutes: minutes);
        (await WithAsync((s, _) => s.InsertAsync(null, [run], Ct).AsTask())).Started.ShouldBeTrue();
        await WithAsync(async (s, _) =>
        {
            await s.MarkRecordedAsync([run.Id], Ct);
            return 0;
        });
        return run.Id;
    }

    [Fact]
    public async Task One_replay_is_active_at_a_time_a_second_start_is_refused_and_stores_nothing()
    {
        var first = await QueueAsync();

        var refused = await WithAsync((s, _) => s.InsertAsync(null, [New(), New()], Ct).AsTask());

        refused.Started.ShouldBeFalse();
        refused.Runs.Select(r => r.Id).ShouldBe([first]);
        (await StoreKit.CountAsync(Db.ConnectionString, "SELECT count(*) FROM research.replay_runs", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_pair_is_stored_together_and_reads_back_in_order_with_the_trial_number_of_its_range()
    {
        var (a, b) = (New(mode: "frozen"), New(mode: "learning", minutes: 1));
        var pair = Guid.NewGuid();

        (await WithAsync((s, _) => s.InsertAsync(pair, [a, b], Ct).AsTask())).Started.ShouldBeTrue();
        var runs = await WithAsync((_, r) => r.GetPairAsync(pair, Ct).AsTask());

        runs.Select(r => (r.Id, r.Mode, r.State, r.TrialNumber, r.SessionsDone, r.SessionsTotal, r.Complete)).ShouldBe([(a.Id, "frozen", "queued", 1, 0, 3, false), (b.Id, "learning", "queued", 2, 0, 3, false)]);
        runs.Select(r => r.InputHash).ShouldAllBe(h => h == new string('a', 64));
    }

    [Fact]
    public async Task A_run_is_claimed_only_when_none_is_running_and_the_oldest_first()
    {
        var first = await QueueAsync(minutes: 0);
        await WithAsync((s, _) => s.CancelAsync(first, T0, Ct).AsTask()); // not active any more, so a second can be queued
        var (second, third) = (New(minutes: 1), New(minutes: 2));
        await WithAsync((s, _) => s.InsertAsync(null, [third, second], Ct).AsTask());
        await WithAsync(async (s, _) =>
        {
            await s.MarkRecordedAsync([third.Id, second.Id], Ct);
            return 0;
        });

        var claimed = await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());

        claimed!.Id.ShouldBe(second.Id);
        claimed.State.ShouldBe("running");
        (await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask())).ShouldBeNull("a run is running: no second claim");
    }

    [Fact]
    public async Task A_session_is_stored_with_its_trades_and_the_cursor_and_storing_it_again_changes_nothing()
    {
        var id = await QueueAsync();
        await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());

        var first = await WithAsync((s, _) => s.SaveSessionAsync(id, Session(D1), [Trade(1, closed: true)], Ct).AsTask());
        var again = await WithAsync((s, _) => s.SaveSessionAsync(id, Session(D1, 5m), [Trade(2, closed: true)], Ct).AsTask());

        first.ShouldBeTrue();
        again.ShouldBeFalse("idempotent by run and session");
        var run = (await WithAsync((_, r) => r.GetRunAsync(id, Ct).AsTask()))!;
        (run.SessionsDone, run.CursorSession, run.State, run.Complete).ShouldBe((1, D1, "running", false));
        (await WithAsync((_, r) => r.SessionAsync(id, D1, Ct).AsTask()))!.Equity.ShouldBe(1_000_000m, "the stored session is the first one");
        (await WithAsync((_, r) => r.TradesAsync(id, Ct).AsTask())).Select(t => t.TradeNo).ShouldBe([1L], "the trades of the repeated call were not written");
        (await WithAsync((s, _) => s.SessionHashesAsync(id, Ct).AsTask())).ShouldBe(new Dictionary<DateOnly, string> { [D1] = new string('c', 64) });
    }

    [Fact]
    public async Task A_session_record_gives_the_same_hash_after_it_has_been_through_jsonb()
    {
        var id = await QueueAsync();
        await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());
        var record = new XDrishti.Domain.Replay.ReplaySessionRecord(D1, [new XDrishti.Domain.Replay.ReplayCandidateRow("AAA", "BUY", "S1|1D|BUY|swing|FIXED_RR", 1)], [], [], [], [], [], 1_000_000.5m, 0.25m, 0.5m, 0, ["settings_now", "before_costs"], []);
        var text = XDrishti.Application.Replay.ReplayJson.Canonical(record);
        var hash = XDrishti.Application.Replay.ReplayJson.Sha256(text);
        await WithAsync((s, _) => s.SaveSessionAsync(id, new ReplaySessionRow(D1, text, record.Equity, record.DrawdownPct, hash), [], Ct).AsTask());

        var back = (await WithAsync((_, r) => r.SessionAsync(id, D1, Ct).AsTask()))!;

        back.RecordJson.ShouldNotBe(text, "jsonb reorders the keys and changes the spacing");
        XDrishti.Application.Replay.ReplayHashes.Session(XDrishti.Application.Replay.ReplayJson.Read<XDrishti.Domain.Replay.ReplaySessionRecord>(back.RecordJson)!).ShouldBe(hash);
    }

    [Fact]
    public async Task A_run_left_running_goes_back_to_the_queue_with_its_cursor_and_resumes_where_it_stopped()
    {
        var id = await QueueAsync();
        await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());
        await WithAsync((s, _) => s.SaveSessionAsync(id, Session(D1), [], Ct).AsTask());
        await WithAsync((s, _) => s.SaveSessionAsync(id, Session(D2), [], Ct).AsTask()); // the process is killed here

        var touched = await WithAsync((s, _) => s.RecoverAsync(T0.AddHours(1), Ct).AsTask());
        var resumed = await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());

        touched.ShouldBe(1);
        (resumed!.Id, resumed.State, resumed.CursorSession, resumed.SessionsDone).ShouldBe((id, "running", D2, 2));
        (await WithAsync((s, _) => s.SaveSessionAsync(id, Session(D3), [], Ct).AsTask())).ShouldBeTrue();
        (await WithAsync((_, r) => r.SessionsAsync(id, D2, null, Ct).AsTask())).Select(x => x.Session).ShouldBe([D2, D3]);
    }

    [Fact]
    public async Task A_run_is_claimed_only_once_it_is_recorded_and_recovery_cancels_one_that_never_was()
    {
        var run = New();
        await WithAsync((s, _) => s.InsertAsync(null, [run], Ct).AsTask());

        (await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask())).ShouldBeNull("its ledger rows do not exist yet");
        (await WithAsync((s, _) => s.RecoverAsync(T0.AddMinutes(2), Ct).AsTask())).ShouldBe(0, "a start that is still writing its ledger rows is given five minutes");
        (await WithAsync((s, _) => s.RecoverAsync(T0.AddMinutes(10), Ct).AsTask())).ShouldBe(1);
        (await WithAsync((s, _) => s.StateAsync(run.Id, Ct).AsTask())).ShouldBe("cancelled");
        (await WithAsync((_, r) => r.AnyRunningAsync(Ct).AsTask())).ShouldBeFalse();
    }

    [Fact]
    public async Task A_start_that_died_before_its_ledger_rows_do_not_block_later_starts_once_it_is_five_minutes_old()
    {
        var recent = New() with { CreatedAt = DateTimeOffset.UtcNow };
        await WithAsync((s, _) => s.InsertAsync(null, [recent], Ct).AsTask());

        var tooSoon = await WithAsync((s, _) => s.InsertAsync(null, [New()], Ct).AsTask());
        tooSoon.Started.ShouldBeFalse("a start that may still be writing its rows holds the slot");

        var old = New() with { CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-6) };
        await StoreKit.ExecuteAsync(Db.ConnectionString, $"UPDATE research.replay_runs SET created_at = now() - interval '6 minutes' WHERE id = '{recent.Id}'", Ct);
        var later = await WithAsync((s, _) => s.InsertAsync(null, [old], Ct).AsTask());

        later.Started.ShouldBeTrue("the stale unrecorded run was cancelled under the lock");
        (await WithAsync((s, _) => s.StateAsync(recent.Id, Ct).AsTask())).ShouldBe("cancelled");
    }

    [Fact]
    public async Task A_cancel_ends_a_queued_run_at_once_and_a_running_run_after_its_session()
    {
        var queued = await QueueAsync();

        (await WithAsync((s, _) => s.CancelAsync(queued, T0, Ct).AsTask())).ShouldBe("cancelled");
        var running = await QueueAsync(minutes: 5);
        await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());
        await WithAsync((s, _) => s.SaveSessionAsync(running, Session(D1), [], Ct).AsTask());

        (await WithAsync((s, _) => s.CancelAsync(running, T0, Ct).AsTask())).ShouldBe("cancelling");
        (await WithAsync((s, _) => s.StateAsync(running, Ct).AsTask())).ShouldBe("cancelling");
        (await WithAsync((s, _) => s.SaveSessionAsync(running, Session(D2), [], Ct).AsTask())).ShouldBeTrue("the session in flight is stored");
        (await WithAsync((s, _) => s.FinishAsync(running, "cancelled", null, null, [Trade(1, closed: false)], T0, Ct).AsTask())).ShouldBeTrue();

        var run = (await WithAsync((_, r) => r.GetRunAsync(running, Ct).AsTask()))!;
        (run.State, run.Complete, run.SessionsDone, run.FinishedAt is not null).ShouldBe(("cancelled", false, 2, true));
        (await WithAsync((s, _) => s.CancelAsync(running, T0, Ct).AsTask())).ShouldBeNull("a finished run has nothing to cancel");
        (await WithAsync((_, r) => r.TradesAsync(running, Ct).AsTask())).Single().ExitSession.ShouldBeNull("an open trade is stored as open");
    }

    [Fact]
    public async Task A_run_completes_with_its_result_hash_and_fails_with_its_reason_and_a_finished_run_cannot_be_changed()
    {
        var done = await QueueAsync();
        await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());
        await WithAsync((s, _) => s.SaveSessionAsync(done, Session(D1), [], Ct).AsTask());

        (await WithAsync((s, _) => s.FinishAsync(done, "completed", new string('d', 64), null, [], T0, Ct).AsTask())).ShouldBeTrue();
        var run = (await WithAsync((_, r) => r.GetRunAsync(done, Ct).AsTask()))!;
        (run.State, run.ResultHash, run.Complete).ShouldBe(("completed", new string('d', 64), true));

        var refused = await Should.ThrowAsync<PostgresException>(async () => await StoreKit.ExecuteAsync(Db.ConnectionString, $"UPDATE research.replay_runs SET state = 'running' WHERE id = '{done}'", Ct)); // a finished run stays final
        refused.SqlState.ShouldBe("23514");
        (await WithAsync((s, _) => s.FinishAsync(done, "failed", null, "x", [], T0, Ct).AsTask())).ShouldBeFalse();

        var broken = await QueueAsync(minutes: 9);
        await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());
        await WithAsync((s, _) => s.FinishAsync(broken, "failed", null, new string('r', 500), [], T0, Ct).AsTask());
        var failed = (await WithAsync((_, r) => r.GetRunAsync(broken, Ct).AsTask()))!;
        (failed.State, failed.FailReason!.Length, failed.ResultHash).ShouldBe(("failed", 400, null));
    }

    [Fact]
    public async Task The_summary_comes_from_the_paper_ledger_over_the_stored_trades_and_a_run_with_no_session_has_none()
    {
        var id = await QueueAsync();
        (await WithAsync((_, r) => r.SummaryAsync(id, Ct).AsTask())).ShouldBeNull();
        await WithAsync((s, _) => s.ClaimAsync(Ct).AsTask());
        await WithAsync((s, _) => s.SaveSessionAsync(id, Session(D1), [], Ct).AsTask());
        await WithAsync((s, _) => s.SaveSessionAsync(id, Session(D2, 1_000_115m), [Trade(1, closed: true, net: 115m)], Ct).AsTask());

        var summary = (await WithAsync((_, r) => r.SummaryAsync(id, Ct).AsTask()))!;

        (summary.Figures.ClosedCount, summary.Figures.OpenCount, summary.Trades).ShouldBe((1, 0, 1));
        summary.Figures.Equity.ShouldBe(1_000_115m);
        summary.Run.CursorSession.ShouldBe(D2);
    }

    [Fact]
    public async Task The_trial_ledger_accepts_the_kind_system_replay_and_refuses_an_unknown_kind()
    {
        await using var scope = Db.Host.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ITestLedger>();
        var row = new LedgerRow(Guid.NewGuid(), new string('a', 64), "S1", 1, new string('1', 64), null, "system replay", D1, D3, LedgerRow.SystemReplayKind, T0, LedgerRow.SystemReplayKind, 0);

        (await ledger.AppendAsync(row, Ct)).ShouldBeTrue();
        var unknown = await Should.ThrowAsync<PostgresException>(async () => await ledger.AppendAsync(row with { RunId = Guid.NewGuid(), TrialKind = "other" }, Ct));
        unknown.SqlState.ShouldBe("23514");

        // each replay is a look and a variant is not: one more replay of the same hash adds a look and no variant (SR-01, ADR 0272)
        await ledger.AppendAsync(row with { RunId = Guid.NewGuid() }, Ct);
        var counts = await ledger.CountTrialsAsync("S1", Ct);
        counts.SystemReplays.ShouldBe(2);
        counts.Total.ShouldBe(2);

        // a replay over several versions writes a row for each, and is one look: the rows of one run share the run half of their id
        var runHalf = Guid.NewGuid().ToString("N")[..16];
        await ledger.AppendAsync(row with { RunId = new Guid(runHalf + Guid.NewGuid().ToString("N")[..16]), StrategyHash = new string('2', 64), Version = 2 }, Ct);
        await ledger.AppendAsync(row with { RunId = new Guid(runHalf + Guid.NewGuid().ToString("N")[..16]), StrategyHash = new string('3', 64), Version = 3 }, Ct);
        (await ledger.CountTrialsAsync("S1", Ct)).SystemReplays.ShouldBe(3, "two earlier replays and one over two versions");
        counts = await ledger.CountTrialsAsync("S1", Ct);
        counts.DistinctHashes.ShouldBe(0);
        (await ledger.CountVariantsAsync("S1", Ct)).ShouldBe(0);
        (await ledger.HasHashAsync("S1", new string('1', 64), Ct)).ShouldBeFalse();
    }
}
