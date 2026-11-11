#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Replay;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Job;

/// <summary>SR-01 part B, AC 1 to 3, 12 and 16: a start stores the run with its input hash, settings snapshot and scope hash, refuses a bad or hold-out range, and records each run in the trial ledger as a look of kind <c>system_replay</c>; the input hash moves with every input.</summary>
public sealed class StartReplayHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ReplayParams Six => ReplayRig.Params(6);

    [Fact]
    public async Task A_start_stores_the_run_as_queued_with_both_hashes_the_snapshot_and_the_session_count()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());

        var started = await rig.StartAsync(Six);

        started.IsSuccess.ShouldBeTrue();
        var run = rig.Store.Of(started.Value.RunIds.Single()).Created;
        run.InputHash.ShouldMatch("^[0-9a-f]{64}$");
        run.ScopeHash.ShouldMatch("^[0-9a-f]{64}$");
        (run.From, run.To, run.Mode, run.SessionsTotal).ShouldBe((ReplayWorld.Start, ReplayWorld.RunSessions[5], "frozen", 6));
        ReplayJson.Read<ReplaySettingsSnapshot>(run.SettingsJson).ShouldNotBeNull();
        rig.Store.Of(run.Id).State.ShouldBe(ReplayRunStates.Queued);
        started.Value.TrialNumber.ShouldBe(1);
    }

    [Fact]
    public async Task Each_started_run_is_a_system_replay_look_in_the_trial_ledger_with_its_input_hash_one_row_per_strategy_version()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());

        var started = await rig.StartAsync(Six);

        var row = rig.Ledger.Rows.ShouldHaveSingleItem();
        (row.TrialKind, row.StrategyId, row.StrategyHash, row.RunKey).ShouldBe((LedgerRow.SystemReplayKind, "S1", ReplayWorld.Hash1, rig.Store.Of(started.Value.RunIds[0]).Created.InputHash));
        (row.From, row.To, row.ExtraTrials).ShouldBe((ReplayWorld.Start, ReplayWorld.RunSessions[5], 0));
    }

    [Fact]
    public async Task The_trial_number_counts_the_replays_over_the_same_range()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());
        var first = await rig.StartAsync(Six);
        rig.Store.Of(first.Value.RunIds[0]).State = ReplayRunStates.Completed;
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await rig.StartAsync(Six);
        rig.Store.Of(second.Value.RunIds[0]).State = ReplayRunStates.Completed;
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        var other = await rig.StartAsync(ReplayRig.Params(5));
        rig.Store.Of(other.Value.RunIds[0]).State = ReplayRunStates.Completed;
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        var third = await rig.StartAsync(Six);

        (first.Value.TrialNumber, second.Value.TrialNumber, other.Value.TrialNumber, third.Value.TrialNumber).ShouldBe((1, 2, 1, 3));
        (await rig.Store.GetRunAsync(third.Value.RunIds[0], Ct))!.TrialNumber.ShouldBe(3);
    }

    [Fact]
    public async Task The_frozen_and_the_learning_run_of_a_request_share_a_pair_id_and_differ_in_their_input_hash()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());

        var started = await rig.StartAsync(Six, ReplayRig.Params(6, mode: ReplayMode.Learning));

        started.IsSuccess.ShouldBeTrue();
        started.Value.PairId.ShouldNotBeNull();
        rig.Store.Runs.Select(r => (r.Pair, r.Created.Mode)).ShouldBe([(started.Value.PairId, "frozen"), (started.Value.PairId, "learning")]);
        rig.Store.Runs.Select(r => r.Created.InputHash).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_range_into_the_hold_out_and_a_bad_range_store_nothing_and_say_why()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());

        var holdOut = await rig.StartAsync(Six with { To = new DateOnly(2027, 3, 1) });
        var bad = await rig.StartAsync(Six with { From = Six.To.AddDays(1) });
        var empty = await rig.StartAsync(Six with { Scope = new ReplayScope([], []) });

        holdOut.Error.Code.ShouldBe("replay.holdout");
        holdOut.Error.Message.ShouldContain("last allowed date");
        bad.Error.Code.ShouldBe("replay.invalid");
        empty.Error.Code.ShouldBe("replay.invalid");
        rig.Store.Runs.ShouldBeEmpty();
        rig.Ledger.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_input_hash_is_stable_for_the_same_inputs_and_moves_with_each_input()
    {
        var baseline = await HashOf(_ => { }, Six);

        (await HashOf(_ => { }, Six)).ShouldBe(baseline);
        (await HashOf(_ => { }, Six with { Capital = 2_000_000m })).ShouldNotBe(baseline, "the parameters");
        (await HashOf(w => w.Fill = w.Fill with { MaxOpenPositions = 3 }, Six)).ShouldNotBe(baseline, "the settings snapshot");
        (await HashOf(w => w.Versions["S1"][0] = ReplayWorld.Version("S1", 1, new string('9', 64), DateTimeOffset.UnixEpoch), Six)).ShouldNotBe(baseline, "the strategy version hash");
        (await HashOf(w => w.Close["AAA"][new DateOnly(2026, 10, 7)] += 0.01, Six)).ShouldNotBe(baseline, "a bar inside the window");
        (await HashOf(w => w.Snapshots[0] = (w.Snapshots[0].AsOf, ReplayWorld.Stats(w.Snapshots[0].AsOf, snapshot: "other")), Six)).ShouldNotBe(baseline, "the frozen statistics snapshot");
        (await HashOf(w =>
        {
            w.Close["CCC"] = new SortedDictionary<DateOnly, double>(w.Close["AAA"]);
            w.Members.Add((ReplayWorld.Ids["CCC"], "CCC", new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero))); // a member that joined and left inside the range
        }, Six)).ShouldNotBe(baseline, "the membership over the range, not only at its ends");
        (await HashOf(w => w.Seams.SeamOfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<DateTimeOffset?>(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero))), Six)).ShouldNotBe(baseline, "the backfill seam of an instrument");
        (await HashOf(w => w.Close["AAA"][new DateOnly(2026, 10, 14)] += 0.01, Six)).ShouldBe(baseline, "a bar after the range is not an input of this run");
    }

    [Fact]
    public async Task A_scope_whose_bars_need_more_memory_than_the_cap_is_refused_naming_the_largest_scope_allowed()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld()) { StartOptions = new SystemReplayOptions { MaxWindowMb = 8 } }; // about 5.9 MB per instrument: one instrument fits, two do not

        var refused = await rig.StartAsync(Six);

        refused.Error.Code.ShouldBe("replay.invalid");
        refused.Error.Message.ShouldContain("largest scope allowed is 1 instruments");
        rig.Store.Runs.ShouldBeEmpty();
        (await new ReplayJobRig(ReplayJobRig.SwingWorld()).StartAsync(Six)).IsSuccess.ShouldBeTrue("the default cap fits the invented two-instrument scope");
    }

    [Fact]
    public async Task A_start_while_the_history_backfill_is_incomplete_is_refused_as_busy()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());
        rig.Gate.Incomplete = "1 instruments unfinished";

        var refused = await rig.StartAsync(Six);

        refused.Error.Code.ShouldBe("replay.busy");
        refused.Error.Message.ShouldContain("backfill");
        rig.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_ledger_rows_of_one_run_share_the_run_half_of_their_id_so_the_ledger_counts_replays_not_versions()
    {
        var world = ReplayJobRig.SwingWorld();
        world.Versions["S1"].Add(ReplayWorld.Version("S1", 2, new string('3', 64), DateTimeOffset.UnixEpoch.AddYears(10)));
        var rig = new ReplayJobRig(world);

        var started = await rig.StartAsync(Six with { VersionRule = ReplayVersionRule.AsOf });
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Error.Message : string.Empty);

        rig.Ledger.Rows.Count.ShouldBe(2, "a row for every version used");
        rig.Ledger.Rows.Select(r => r.RunId.ToString()[..18]).Distinct().Count().ShouldBe(1);
        rig.Ledger.Rows.Select(r => r.RunId).Distinct().Count().ShouldBe(2);
    }

    private static async Task<string> HashOf(Action<ReplayWorld> tune, ReplayParams p)
    {
        var world = ReplayJobRig.SwingWorld();
        tune(world);
        var rig = new ReplayJobRig(world);
        var started = await rig.StartAsync(p);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Error.Code + started.Error.Message : string.Empty);
        return rig.Store.Of(started.Value.RunIds[0]).Created.InputHash;
    }
}
