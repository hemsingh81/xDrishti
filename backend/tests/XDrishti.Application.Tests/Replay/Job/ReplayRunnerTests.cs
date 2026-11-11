using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Application.SimAll;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Tests.Replay.Job;

/// <summary>SR-01 part B: the job steps one session at a time, stores the cursor, resumes after a kill with byte-identical records, cancels after the current session, stores the reason of a failed session and stops, and yields under the rules of the simulate-everything job (AC 13, 15, 16).</summary>
public sealed class ReplayRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D1 = new(2026, 10, 5);
    private static readonly DateOnly D2 = new(2026, 10, 6);
    private static readonly DateOnly D3 = new(2026, 10, 7);
    private static readonly DateOnly D6 = new(2026, 10, 12);

    private static ReplayParams Six => ReplayRig.Params(6);

    private static async Task<(ReplayJobRig Rig, Guid Id)> Queued(Action<ReplayJobRig>? tune = null, ReplayParams? parameters = null)
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());
        tune?.Invoke(rig);
        var started = await rig.StartAsync(parameters ?? Six);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Error.Code + ": " + started.Error.Message : string.Empty);
        return (rig, started.Value.RunIds[0]);
    }

    private static async Task<FakeReplayStore.Run> Finished(ReplayJobRig rig, Guid id)
    {
        var runner = rig.Runner();
        await runner.RecoverAsync(Ct);
        await runner.TickAsync(Ct);
        return rig.Store.Of(id);
    }

    [Fact]
    public async Task A_run_is_stored_one_session_at_a_time_and_ends_completed_with_the_hash_of_its_records()
    {
        var (rig, id) = await Queued();
        var direct = await ReplayRig.Create(rig.World).Driver.RunAsync(Six, Ct); // the same run in memory, for the expected records

        var run = await Finished(rig, id);

        run.State.ShouldBe(ReplayRunStates.Completed);
        run.Sessions.Keys.ToList().ShouldBe([.. direct.Value.Sessions.Select(s => s.Session)]);
        run.Sessions.Values.Select(s => s.RecordJson).ShouldBe(direct.Value.Sessions.Select(ReplayJson.Canonical));
        run.ResultHash.ShouldBe(ReplayHashes.Result(run.Sessions.Values.Select(s => s.RecordSha256)));
        run.Cursor.ShouldBe(D6);
        run.FinishedAt.ShouldNotBeNull();
        run.Sessions[D6].Equity.ShouldBe(direct.Value.Final.Equity!.Value);
        var trade = run.Trades.ShouldHaveSingleItem();
        (trade.Symbol, trade.EntrySession, trade.ExitSession).ShouldBe(("AAA", D2, D6));
        (await rig.Store.GetRunAsync(id, Ct))!.Complete.ShouldBeTrue();
    }

    [Fact]
    public async Task Two_runs_with_the_same_input_hash_give_the_same_result_hash()
    {
        var (rig, first) = await Queued();
        await Finished(rig, first);
        var second = (await rig.StartAsync(Six)).Value.RunIds[0];
        await Finished(rig, second);

        var (a, b) = (rig.Store.Of(first), rig.Store.Of(second));

        a.Created.InputHash.ShouldBe(b.Created.InputHash);
        a.ResultHash.ShouldNotBeNull();
        b.ResultHash.ShouldBe(a.ResultHash);
    }

    [Fact]
    public async Task A_run_killed_after_three_sessions_resumes_at_the_fourth_and_its_records_are_byte_identical_to_an_unbroken_run()
    {
        var (whole, wholeId) = await Queued();
        var expected = await Finished(whole, wholeId);

        var (rig, id) = await Queued();
        using var kill = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        rig.Store.AfterSave = (_, stored) =>
        {
            if (stored == 3)
            {
                kill.Cancel(); // the process dies between two sessions
            }
        };
        var first = rig.Runner();
        await first.RecoverAsync(Ct);
        await Should.ThrowAsync<OperationCanceledException>(async () => await first.TickAsync(kill.Token));
        var interrupted = rig.Store.Of(id);
        (interrupted.State, interrupted.Sessions.Count, interrupted.Cursor).ShouldBe((ReplayRunStates.Running, 3, D3));

        rig.Store.AfterSave = null;
        var second = rig.Runner(); // a new process: nothing of the book is in memory
        await second.RecoverAsync(Ct);
        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Queued, "a run left running goes back to the queue with its cursor");
        await second.TickAsync(Ct);

        var resumed = rig.Store.Of(id);
        resumed.State.ShouldBe(ReplayRunStates.Completed);
        resumed.Sessions.Values.Select(s => s.RecordJson).ShouldBe(expected.Sessions.Values.Select(s => s.RecordJson));
        resumed.ResultHash.ShouldBe(expected.ResultHash);
        resumed.Trades.Select(t => (t.TradeNo, t.ExitSession)).ShouldBe(expected.Trades.Select(t => (t.TradeNo, t.ExitSession)));
        rig.Store.SaveCalls.ShouldBe(6, "three sessions before the kill and three after it: a stored session is not written again");
    }

    [Fact]
    public async Task A_cancel_stops_the_run_after_the_current_session_keeps_the_finished_ones_and_it_reads_cancelled()
    {
        var (rig, id) = await Queued(r => r.Store.CancelAfterSessions = 2);

        var run = await Finished(rig, id);

        run.State.ShouldBe(ReplayRunStates.Cancelled);
        run.Sessions.Keys.ToList().ShouldBe([D1, D2]);
        run.Cursor.ShouldBe(D2);
        run.ResultHash.ShouldBeNull();
        run.FinishedAt.ShouldNotBeNull();
        (await rig.Store.GetRunAsync(id, Ct))!.Complete.ShouldBeFalse();
        (await rig.Runner().RunOnceClaimsNothing()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_cancel_of_a_queued_run_never_starts_it()
    {
        var (rig, id) = await Queued();

        (await new CancelReplayHandler(rig.Store, rig.Store, rig.Time).HandleAsync(new CancelReplayCommand(id), Ct)).Value.ShouldBe(ReplayRunStates.Cancelled);
        var run = await Finished(rig, id);

        (run.State, run.Sessions.Count).ShouldBe((ReplayRunStates.Cancelled, 0));
        (await new CancelReplayHandler(rig.Store, rig.Store, rig.Time).HandleAsync(new CancelReplayCommand(id), Ct)).Error.Code.ShouldBe("replay.not_active");
        (await new CancelReplayHandler(rig.Store, rig.Store, rig.Time).HandleAsync(new CancelReplayCommand(Guid.NewGuid()), Ct)).Error.Code.ShouldBe("replay.not_found");
    }

    [Fact]
    public async Task A_failed_session_stores_its_reason_and_stops_the_run_and_nothing_after_it_is_replayed()
    {
        var (rig, id) = await Queued(r => r.Candidates = f => new ThrowingCandidates(new BarCandidates(f), D3));

        var run = await Finished(rig, id);

        run.State.ShouldBe(ReplayRunStates.Failed);
        run.FailReason!.ShouldContain("session 2026-10-07");
        run.FailReason!.ShouldContain("replay.session_crashed");
        run.Sessions.Keys.ToList().ShouldBe([D1, D2], "the failed session is not stored and no later session is run: nothing is skipped");
        (run.ResultHash, run.FinishedAt is not null).ShouldBe((null, true));
        (await rig.Store.GetRunAsync(id, Ct))!.Complete.ShouldBeFalse();
    }

    [Fact]
    public async Task A_session_that_replays_to_another_record_than_the_stored_one_fails_the_run_instead_of_being_mixed_in()
    {
        var (rig, id) = await Queued();
        using var kill = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        rig.Store.AfterSave = (_, stored) =>
        {
            if (stored == 2)
            {
                kill.Cancel();
            }
        };
        var first = rig.Runner();
        await Should.ThrowAsync<OperationCanceledException>(async () => await first.TickAsync(kill.Token));
        var run = rig.Store.Of(id);
        run.Sessions[D1] = run.Sessions[D1] with { RecordSha256 = new string('f', 64) }; // a stored session that the replay cannot reproduce
        rig.Store.AfterSave = null;
        var second = rig.Runner();
        await second.RecoverAsync(Ct);

        await second.TickAsync(Ct);

        run.State.ShouldBe(ReplayRunStates.Failed);
        run.FailReason!.ShouldContain("replay.diverged");
        run.Sessions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_stored_snapshot_is_used_on_resume_not_todays_settings()
    {
        var (rig, id) = await Queued();
        rig.World.Fill = rig.World.Fill with { MaxOpenPositions = 3 }; // the settings of today change after the start

        var run = await Finished(rig, id);

        run.State.ShouldBe(ReplayRunStates.Completed);
        var snapshot = ReplayJson.Read<ReplaySettingsSnapshot>(run.Created.SettingsJson)!;
        snapshot.Fill.MaxOpenPositions.ShouldBe(1);
        ReplayJson.Canonical(snapshot).ShouldBe(run.Created.SettingsJson, "the snapshot round-trips through its stored text");
        ReplayJson.Read<ReplaySessionRecord>(run.Sessions[D3].RecordJson)!.Taken.ShouldBeEmpty(); // the held slot (one position) still blocks, as at the start
    }

    [Theory]
    [InlineData("disk")]
    [InlineData("data_job")]
    [InlineData("sim_all")]
    [InlineData("market_hours")]
    [InlineData("backfill")]
    public async Task The_job_does_not_claim_a_run_while_a_yield_rule_says_wait_and_goes_on_when_it_lifts(string rule)
    {
        var (rig, id) = await Queued();
        void Apply(bool on)
        {
            switch (rule)
            {
                case "disk": rig.Disk.Free = on ? 1 : 500L * SimAllSettings.Gb; break;
                case "data_job": rig.Gate.DataJob = on; break;
                case "sim_all": rig.SimAll.Seed(1, 1, on ? SimAllStates.Running : SimAllStates.Completed); break;
                case "market_hours": rig.Time.SetUtcNow(on ? ReplayJobRig.MarketMorning : ReplayJobRig.Evening); break;
                default: rig.Gate.Incomplete = on ? "unfinished" : null; break;
            }
        }

        var runner = rig.Runner();
        Apply(true);
        await runner.TickAsync(Ct);
        (rig.Store.Of(id).State, rig.Store.Of(id).Sessions.Count).ShouldBe((ReplayRunStates.Queued, 0));

        Apply(false);
        rig.Time.Advance(TimeSpan.FromMinutes(2)); // the backfill answer is cached for a minute
        await runner.TickAsync(Ct);
        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Completed);
    }

    [Fact]
    public async Task A_run_in_flight_waits_between_sessions_keeps_its_book_in_memory_and_does_not_replay_stored_sessions_when_it_goes_on()
    {
        var (rig, id) = await Queued();
        rig.Store.AfterSave = (_, stored) =>
        {
            if (stored == 2)
            {
                rig.Gate.DataJob = true; // a bar-history job or a recovery pass starts
            }
        };
        var runner = rig.Runner();

        await runner.TickAsync(Ct);
        (rig.Store.Of(id).State, rig.Store.Of(id).Sessions.Count).ShouldBe((ReplayRunStates.Running, 2));

        rig.Gate.DataJob = false;
        rig.Store.AfterSave = null;
        await runner.TickAsync(Ct);

        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Completed);
        rig.Store.SaveCalls.ShouldBe(6);
    }

    [Fact]
    public async Task A_second_start_while_a_replay_is_queued_or_running_is_refused_as_busy_and_stores_and_records_nothing()
    {
        var (rig, _) = await Queued();
        var rows = rig.Ledger.Rows.Count;

        var again = await rig.StartAsync(Six);

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("replay.busy");
        rig.Store.Runs.Count.ShouldBe(1);
        rig.Ledger.Rows.Count.ShouldBe(rows);
    }
}

internal sealed class ThrowingCandidates(XDrishti.Application.Abstractions.Replay.IReplayCandidates inner, DateOnly on) : XDrishti.Application.Abstractions.Replay.IReplayCandidates
{
    public ValueTask<XDrishti.Application.Abstractions.Replay.ReplayCandidateSet> ReadAsync(ReplayParams parameters, DateOnly session, DateOnly forDate, CancellationToken cancellationToken) =>
        session == on ? throw new InvalidOperationException("the candidates of this session cannot be read") : inner.ReadAsync(parameters, session, forDate, cancellationToken);
}
