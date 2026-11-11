using NSubstitute;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Application.SimAll;
using XDrishti.Application.Tests.SimAll;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_save_does_not_leave_a_book_that_stepped_past_the_store_the_next_tick_rebuilds_it_and_the_records_are_unchanged(bool afterStoring)
    {
        var (whole, wholeId) = await Queued();
        var expected = await Finished(whole, wholeId);
        var (rig, id) = await Queued();
        rig.Store.FailSaves = 0;
        rig.Store.AfterSave = (_, stored) =>
        {
            if (stored == 2 && rig.Store.SaveCalls == 2)
            {
                rig.Store.FailSaves = 1; // the third save fails
                rig.Store.FailAfterStoring = afterStoring;
            }
        };
        var runner = rig.Runner();

        await runner.TickAsync(Ct);
        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Running, "one failure does not end the run");
        rig.Store.AfterSave = null;
        rig.Time.Advance(TimeSpan.FromSeconds(6)); // the wait after a failure
        await runner.TickAsync(Ct);

        var run = rig.Store.Of(id);
        run.State.ShouldBe(ReplayRunStates.Completed);
        run.Sessions.Values.Select(s => s.RecordJson).ShouldBe(expected.Sessions.Values.Select(s => s.RecordJson));
        run.ResultHash.ShouldBe(expected.ResultHash);
        run.Trades.Select(t => t.TradeNo).ShouldBe(expected.Trades.Select(t => t.TradeNo));
    }

    [Fact]
    public async Task A_store_that_keeps_failing_ends_the_run_failed_with_the_reason_after_repeated_failures_each_after_a_wait()
    {
        var (rig, id) = await Queued(r => r.Store.FailSaves = int.MaxValue);
        var runner = rig.Runner();

        for (var i = 0; i < 8; i++)
        {
            rig.Time.Advance(TimeSpan.FromMinutes(6)); // past the longest wait
            await runner.TickAsync(Ct);
        }

        var run = rig.Store.Of(id);
        run.State.ShouldBe(ReplayRunStates.Failed);
        run.FailReason!.ShouldContain("replay.store_failed");
    }

    [Fact]
    public async Task A_short_outage_of_the_store_does_not_fail_the_run_a_failure_waits_before_the_next_try()
    {
        var (rig, id) = await Queued(r => r.Store.FailSaves = 2);
        var runner = rig.Runner();

        await runner.TickAsync(Ct); // fails, waits
        await runner.TickAsync(Ct); // still waiting: nothing is tried
        rig.Store.SaveCalls.ShouldBe(1);
        rig.Time.Advance(TimeSpan.FromSeconds(6));
        await runner.TickAsync(Ct); // fails again, waits longer
        rig.Time.Advance(TimeSpan.FromSeconds(11));
        await runner.TickAsync(Ct);

        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Completed);
    }

    [Fact]
    public async Task A_cancel_is_honoured_while_the_run_waits_after_a_failure()
    {
        var (rig, id) = await Queued(r => r.Store.FailSaves = int.MaxValue);
        var runner = rig.Runner();
        await runner.TickAsync(Ct); // fails and backs off
        rig.Store.Of(id).State = ReplayRunStates.Cancelling;

        await runner.TickAsync(Ct); // no time has passed

        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Cancelled);
    }

    [Fact]
    public async Task A_refresh_of_the_statistics_after_the_start_does_not_fail_a_learning_run()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());
        var started = await rig.StartAsync(ReplayRig.Params(6, mode: ReplayMode.Learning));
        var id = started.Value.RunIds[0];
        var stored = rig.Store.Of(id).Created.LearningSources;
        rig.World.Snapshots.Add((new DateOnly(2026, 10, 11), ReplayWorld.Stats(new DateOnly(2026, 10, 11), snapshot: "weekly") with { Sources = "other:swing:FIXED_RR{rr=2}" }));

        var run = await Finished(rig, id);

        stored.ShouldBe("run:swing:FIXED_RR{rr=2}");
        run.State.ShouldBe(ReplayRunStates.Completed, "the input hash is taken again from the stored sources, not from today's newest snapshot");
    }

    [Fact]
    public async Task A_failed_recovery_is_tried_again_at_the_next_tick_so_a_stale_running_row_does_not_block_the_claim()
    {
        var (rig, id) = await Queued();
        rig.Store.Of(id).State = ReplayRunStates.Running; // left by a process that died
        rig.Store.FailRecoverOnce = true;
        var runner = rig.Runner();

        await Should.ThrowAsync<InvalidOperationException>(async () => await runner.TickAsync(Ct));
        await runner.TickAsync(Ct);

        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Completed);
    }

    [Fact]
    public async Task A_cancel_that_arrives_while_the_stored_sessions_are_replayed_is_honoured_before_the_rebuild_ends()
    {
        var (rig, id) = await Queued();
        using var kill = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        rig.Store.AfterSave = (_, stored) =>
        {
            if (stored == 3)
            {
                kill.Cancel();
            }
        };
        var first = rig.Runner();
        await Should.ThrowAsync<OperationCanceledException>(async () => await first.TickAsync(kill.Token));
        rig.Store.AfterSave = null;
        var second = rig.Runner();
        await second.RecoverAsync(Ct);
        var looks = 0;
        rig.Store.OnState = (_, _) =>
        {
            if (++looks == 2)
            {
                rig.Store.Of(id).State = ReplayRunStates.Cancelling; // the owner cancels while the second process rebuilds the first session
            }
        };
        await second.TickAsync(Ct);

        (rig.Store.Of(id).State, rig.Store.Of(id).Sessions.Count).ShouldBe((ReplayRunStates.Cancelled, 3));
    }

    [Fact]
    public async Task A_run_that_was_never_recorded_in_the_ledger_is_not_claimed_and_recovery_cancels_it()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());
        var started = await rig.StartAsync(Six);
        var id = started.Value.RunIds[0];
        var runner = rig.Runner();
        await runner.RecoverAsync(Ct);
        rig.Store.Of(id).Recorded = false; // the start died between the insert and the ledger rows

        await runner.TickAsync(Ct);
        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Queued);
        await runner.RecoverAsync(Ct);

        rig.Store.Of(id).State.ShouldBe(ReplayRunStates.Cancelled);
    }

    [Fact]
    public async Task A_run_whose_bars_changed_after_the_start_fails_as_diverged_and_one_whose_version_changed_uses_the_stored_version()
    {
        var (rig, id) = await Queued();
        rig.World.Versions["S1"].Add(ReplayWorld.Version("S1", 2, new string('7', 64), DateTimeOffset.UnixEpoch.AddYears(60))); // a newer active version appears after the start
        var run = await Finished(rig, id);
        run.State.ShouldBe(ReplayRunStates.Completed, "the stored version map is used, not today's active version");

        var (changed, changedId) = await Queued();
        changed.World.Close["AAA"][new DateOnly(2026, 10, 7)] += 0.01; // a bar inside the window is corrected after the start
        var failed = await Finished(changed, changedId);

        failed.State.ShouldBe(ReplayRunStates.Failed);
        failed.FailReason!.ShouldContain("replay.diverged");
        failed.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_simulate_everything_job_does_not_start_a_chunk_while_a_replay_runs()
    {
        var (rig, id) = await Queued();
        rig.Store.Of(id).State = ReplayRunStates.Running;
        rig.SimAll.Seed(1, 3);
        var executor = new FakeExecutor();
        var calendar = NSubstitute.Substitute.For<XDrishti.Application.Abstractions.Market.Calendar.ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new XDrishti.Domain.Market.Calendar.TradingCalendar());
        var scopes = new FakeScopes(
            (typeof(XDrishti.Application.Abstractions.SimAll.ISimAllStore), rig.SimAll), (typeof(XDrishti.Application.Abstractions.Lab.ITestLedger), rig.Ledger), (typeof(XDrishti.Application.Abstractions.SimAll.ISimAllBackfillGate), rig.Gate),
            (typeof(XDrishti.Application.Abstractions.SimAll.IFreeDiskProbe), rig.Disk), (typeof(XDrishti.Application.SimAll.ISimAllChunkExecutor), executor), (typeof(XDrishti.Application.Abstractions.Market.Calendar.ITradingCalendar), calendar),
            (typeof(IReplayReads), rig.Store));
        var simAll = new XDrishti.Application.SimAll.SimAllRunner(scopes, new SimAllSettings(), rig.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<XDrishti.Application.SimAll.SimAllRunner>.Instance);

        await simAll.TickAsync(Ct);

        executor.Ran.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stored_record_gives_the_same_hash_after_the_round_trip_through_the_database_text()
    {
        var direct = await ReplayRig.Create(ReplayJobRig.SwingWorld()).Driver.RunAsync(Six, Ct);

        foreach (var record in direct.Value.Sessions)
        {
            var text = ReplayJson.Canonical(record);
            var reordered = Reverse(System.Text.Json.Nodes.JsonNode.Parse(text))!.ToJsonString(); // jsonb changes the order of the keys; the hash is over the canonical form, which is rebuilt from the value
            reordered.ShouldNotBe(text, "the keys really moved");
            ReplayHashes.Session(ReplayJson.Read<ReplaySessionRecord>(reordered)!).ShouldBe(ReplayHashes.Session(record));
        }
    }

    private static System.Text.Json.Nodes.JsonNode? Reverse(System.Text.Json.Nodes.JsonNode? node) => node switch
    {
        System.Text.Json.Nodes.JsonObject o => new System.Text.Json.Nodes.JsonObject(o.Reverse().Select(p => KeyValuePair.Create(p.Key, Reverse(p.Value)))),
        System.Text.Json.Nodes.JsonArray a => new System.Text.Json.Nodes.JsonArray([.. a.Select(Reverse)]),
        _ => node?.DeepClone(),
    };
}

internal sealed class ThrowingCandidates(XDrishti.Application.Abstractions.Replay.IReplayCandidates inner, DateOnly on) : XDrishti.Application.Abstractions.Replay.IReplayCandidates
{
    public ValueTask<XDrishti.Application.Abstractions.Replay.ReplayCandidateSet> ReadAsync(ReplayParams parameters, DateOnly session, DateOnly forDate, CancellationToken cancellationToken) =>
        session == on ? throw new InvalidOperationException("the candidates of this session cannot be read") : inner.ReadAsync(parameters, session, forDate, cancellationToken);
}
