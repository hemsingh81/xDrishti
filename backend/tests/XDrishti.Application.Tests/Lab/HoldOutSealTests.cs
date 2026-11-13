#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>HO-01b (ADR 0283): the pre-dating horizon at the unlock, the strategy auto look and the auto release at the seal, the seal runner and the stalled-block notice.</summary>
public sealed class HoldOutSealTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly DateOnly One = new(2026, 5, 1);
    private static readonly DateOnly OneEnd = new(2026, 10, 30);
    private static readonly DateOnly Two = new(2026, 10, 31);
    private static readonly DateOnly TwoEnd = new(2027, 4, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ILabRunStore _runs = Substitute.For<ILabRunStore>();
    private readonly ILabResultStore _results = Substitute.For<ILabResultStore>();
    private readonly IPlanFreezeStore _freezes = Substitute.For<IPlanFreezeStore>();
    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();
    private readonly IAccountUnitOfWork _unitOfWork = Substitute.For<IAccountUnitOfWork>();
    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();
    private readonly FixedPass _pass = new();
    private readonly MemoryHoldOutStore _store = new();
    private readonly List<LabRun> _listed = [];
    private readonly Dictionary<string, LabRun> _jobs = [];
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 5, 5, 6, 0, 0, TimeSpan.Zero));

    public HoldOutSealTests()
    {
        // block 1 released, block 2 in force and sealed (its last session closed on 2027-04-30)
        _store.Blocks.Add(new HoldOutBlock(One, new DateOnly(2024, 10, 1), OneEnd, 6, Hash, DateTimeOffset.UnixEpoch));
        _store.Blocks.Add(new HoldOutBlock(Two, new DateOnly(2024, 10, 1), TwoEnd, 6, Hash, DateTimeOffset.UnixEpoch, One));
        _store.Releases.Add(new StoredRelease(One, DateTimeOffset.UnixEpoch, "owner", "owner:x", Hash, true, Two));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(2, 1000, new DateTimeOffset(2024, 10, 1, 9, 15, 0, TimeSpan.FromHours(5.5)), new DateTimeOffset(2027, 4, 30, 15, 29, 0, TimeSpan.FromHours(5.5)), 0, 0));
        _freezes.FirstAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new string('f', 64));
        _runs.ListSucceededFullTestsAsync(Arg.Any<CancellationToken>()).Returns(_ => (IReadOnlyList<LabRun>)[.. _listed]);
        // real key semantics, as the table has them: a second post with the same key gets the first one back and creates nothing
        _runs.InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var n = c.Arg<NewLabRun>();
            if (_jobs.TryGetValue(n.Key, out var existing))
            {
                return new InsertedLabRun(existing, false);
            }

            return new InsertedLabRun(_jobs[n.Key] = Queued(n), true);
        });
    }

    private sealed class FixedPass : IHoldOutPass
    {
        public ValueTask<Result<HoldOutFigures>> RunAsync(LabRun run, LabVerdictView verdict, HoldOutBlock block, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success(new HoldOutFigures(45, 0.12, Two, TwoEnd)));
    }

    private HoldOutEpochs Epochs(FakeTimeProvider? time = null) => new(_store, new FixedCalendar(), time ?? _time);

    private HoldOutUnlocker Unlocker(HoldOutOptions? options = null)
    {
        var o = options ?? new HoldOutOptions();
        return new HoldOutUnlocker(_runs, _results, _store, _freezes, _pass, new LabHoldOutGuard(_store, _bars, o, _time), Epochs(), o, _time, _events, _unitOfWork, NullLogger<HoldOutUnlocker>.Instance);
    }

    private HoldOutStrategyCandidates Candidates() => new(_runs, _results, _freezes, _store);

    private HoldOutReleaser Releaser(HoldOutOptions? options = null) =>
        new(Epochs(), _store, [new StrategyLookSlot(_store, Candidates())], options ?? new HoldOutOptions(), _time, _events, _unitOfWork, NullLogger<HoldOutReleaser>.Instance);

    private StrategyAutoLookStep Step(HoldOutOptions? options = null, FakeTimeProvider? time = null)
    {
        var o = options ?? new HoldOutOptions();
        return new StrategyAutoLookStep(Epochs(time), Candidates(), Unlocker(o), _store, _runs, o, _time, NullLogger<StrategyAutoLookStep>.Instance);
    }

    private static LabRun Queued(NewLabRun n) => new(n.Id, n.Key, n.StrategyId, n.Version, n.StrategyHash, n.Universe, n.From, n.To, n.Method, n.ConfigHash, n.EngineVersion, LabRunState.Queued, null, 0, 0, [], null, [], [], n.Labels, null, false, n.CreatedAt, null, null);

    private LabRun Run(string strategy, int version, DateOnly? frozen, bool passes = true, DateOnly? to = null)
    {
        var run = new LabRun(Guid.NewGuid(), new string('k', 64), strategy, version, Hash, new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null),
            new DateOnly(2024, 10, 1), to ?? new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-5", LabRunState.Succeeded, null, 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, null, null);
        _runs.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);
        _results.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(HoldOutUnlockerTests.Verdict(outcome: passes ? "PASS" : "FAIL"));
        _freezes.FirstFrozenAtAsync(strategy, version, Arg.Any<CancellationToken>()).Returns(frozen is { } d ? new DateTimeOffset(d.ToDateTime(new TimeOnly(5, 0)), TimeSpan.Zero) : (DateTimeOffset?)null);
        _listed.Add(run);
        return run;
    }

    private void FreshStrategyLook(DateOnly block) =>
        _store.Unlocks.Add(new StoredUnlock("ORB", 9, "strategy", "owner", "owner:x", DateTimeOffset.UnixEpoch.AddDays(10), Hash, false, block, Guid.NewGuid(), View(block)));

    private static HoldOutResultView View(DateOnly block) => new("ORB", 9, "strategy", "owner", "used", DateTimeOffset.UnixEpoch, block, block, false, false, true, "pass", "ok", null, null, null, 0, null, Hash, "owner:x", []);

    // ---- the horizon at the unlock

    [Fact]
    public async Task At_a_later_block_a_version_frozen_before_its_start_is_fresh_and_one_frozen_after_is_re_used_with_the_reason()
    {
        var v3 = Run("ORB", 3, new DateOnly(2026, 10, 1));
        var v4 = Run("ORB", 4, new DateOnly(2026, 12, 15));

        var late = await Unlocker().UnlockAsync(v4.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);
        var early = await Unlocker().UnlockAsync(v3.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        (late.Value.ReusedWindow, late.Value.ReusedReason, late.Value.Passed).ShouldBe((true, "not_predated", false));
        (early.Value.ReusedWindow, early.Value.ReusedReason, early.Value.Passed).ShouldBe((false, null, true));
        _store.Unlocks.Single(u => u.Version == 4).Reason.ShouldBe("not_predated");
        _store.Unlocks.Single(u => u.Version == 3).Reason.ShouldBeNull();
    }

    [Fact]
    public async Task A_version_with_no_known_freeze_time_is_never_fresh_at_a_later_block()
    {
        var run = Run("ORB", 3, null);

        var look = await Unlocker().UnlockAsync(run.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        (look.Value.ReusedWindow, look.Value.ReusedReason).ShouldBe((true, "not_predated"));
    }

    [Fact]
    public async Task A_second_strategy_look_at_the_block_is_re_used_and_a_version_looks_once_per_block()
    {
        var a = Run("ORB", 3, new DateOnly(2026, 10, 1));
        var b = Run("ORB", 2, new DateOnly(2026, 9, 1));
        await Unlocker().UnlockAsync(a.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        var second = await Unlocker().UnlockAsync(b.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);
        var again = await Unlocker().UnlockAsync(a.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        second.Value.ReusedWindow.ShouldBeTrue();
        second.Value.ReusedReason.ShouldBeNull("it was predated; its slot was taken");
        again.Error.Code.ShouldBe(LabErrors.HoldOutUsedCode);
    }

    [Fact]
    public async Task A_version_that_looked_at_block_one_may_look_at_block_two()
    {
        var run = Run("ORB", 3, new DateOnly(2026, 4, 1));
        _store.Unlocks.Add(new StoredUnlock("ORB", 3, "strategy", "owner", "owner:x", DateTimeOffset.UnixEpoch, Hash, false, One, Guid.NewGuid(), View(One)));

        var look = await Unlocker().UnlockAsync(run.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        look.Value.ReusedWindow.ShouldBeFalse();
        _store.Unlocks.Count.ShouldBe(2);
    }

    // ---- the strategy auto look at the seal

    [Fact]
    public async Task The_seal_claims_the_look_for_the_pre_dated_version_and_not_the_later_one_and_records_it_as_auto()
    {
        Run("ORB", 4, new DateOnly(2026, 12, 15));
        var v3 = Run("ORB", 3, new DateOnly(2026, 10, 1));

        var said = await Step().RunAsync(Ct);

        said.ShouldBe("look queued for ORB v3");
        await _runs.Received(1).InsertOrGetAsync(Arg.Is<NewLabRun>(n => n.StrategyId == "ORB" && n.Version == 3 && n.Method == HoldOutJob.Method && n.Labels[0] == HoldOutJob.Label(v3.Id, "system:auto-unlock")), Arg.Any<CancellationToken>());
        HoldOutJob.ModeOf("system:auto-unlock").ShouldBe("auto");
        HoldOutJob.ModeOf("owner").ShouldBe("owner");
    }

    [Fact]
    public async Task With_the_auto_look_off_nothing_is_claimed()
    {
        Run("ORB", 3, new DateOnly(2026, 10, 1));

        var said = await Step(new HoldOutOptions(AutoUnlock: false)).RunAsync(Ct);

        said.ShouldBe("auto look off");
        await _runs.DidNotReceive().InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Block_one_is_never_claimed_even_with_a_passing_version()
    {
        _store.Releases.Clear();
        _store.Blocks.RemoveAll(b => b.Start == Two);
        Run("ORB", 3, new DateOnly(2026, 4, 1));
        var later = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero));

        var said = await Step(time: later).RunAsync(Ct);

        said.ShouldBe("block 1 is the owner's");
        await _runs.DidNotReceive().InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_filling_block_and_a_used_slot_and_a_run_that_does_not_pass_claim_nothing()
    {
        Run("ORB", 3, new DateOnly(2026, 10, 1), passes: false);
        (await Step().RunAsync(Ct)).ShouldBe("no candidate");
        var filling = new FakeTimeProvider(new DateTimeOffset(2027, 4, 1, 6, 0, 0, TimeSpan.Zero));
        (await Step(time: filling).RunAsync(Ct)).ShouldBe("block filling");
        FreshStrategyLook(Two);

        (await Step().RunAsync(Ct)).ShouldBe("slot used");

        await _runs.DidNotReceive().InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_run_whose_window_reaches_the_block_is_not_a_candidate()
    {
        Run("ORB", 3, new DateOnly(2026, 10, 1), to: new DateOnly(2026, 11, 15));

        (await Step().RunAsync(Ct)).ShouldBe("no candidate");
    }

    [Fact]
    public async Task A_run_that_looked_at_block_two_is_queued_again_at_block_three_and_the_same_block_is_queued_once()
    {
        var v3 = Run("ORB", 3, new DateOnly(2026, 10, 1));
        (await Step().RunAsync(Ct)).ShouldBe("look queued for ORB v3");
        (await Step().RunAsync(Ct)).ShouldBe("look already queued for ORB v3"); // the same run at the same block gets its queued job back: nothing new is claimed
        _store.Unlocks.Add(new StoredUnlock("ORB", 3, "strategy", "auto", "system:auto-unlock", DateTimeOffset.UnixEpoch.AddDays(1), Hash, false, Two, v3.Id, View(Two)));
        _store.Releases.Add(new StoredRelease(Two, DateTimeOffset.UnixEpoch.AddDays(2), "auto", "system:auto-release", Hash, true, new DateOnly(2027, 5, 1)));
        _store.Blocks.Add(new HoldOutBlock(new DateOnly(2027, 5, 1), new DateOnly(2024, 10, 1), new DateOnly(2027, 10, 31), 6, Hash, DateTimeOffset.UnixEpoch, Two));
        _time.SetUtcNow(new DateTimeOffset(2027, 11, 5, 6, 0, 0, TimeSpan.Zero));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(2, 1000, new DateTimeOffset(2024, 10, 1, 9, 15, 0, TimeSpan.FromHours(5.5)), new DateTimeOffset(2027, 10, 29, 15, 29, 0, TimeSpan.FromHours(5.5)), 0, 0));

        var said = await Step().RunAsync(Ct);

        said.ShouldBe("look queued for ORB v3");
        _jobs.Count.ShouldBe(2, "one job per run and block");
        _jobs.Keys.ShouldContain(HoldOutJob.KeyOf(v3.Id, Two));
        _jobs.Keys.ShouldContain(HoldOutJob.KeyOf(v3.Id, new DateOnly(2027, 5, 1)));
    }

    [Fact]
    public async Task The_newest_run_of_a_version_decides_so_an_older_passing_run_never_outweighs_a_newer_failing_one()
    {
        Run("ORB", 3, new DateOnly(2026, 10, 1), passes: false); // newest
        Run("ORB", 3, new DateOnly(2026, 10, 1), passes: true);

        (await Step().RunAsync(Ct)).ShouldBe("no candidate");
        (await new StrategyLookSlot(_store, Candidates()).ReadAsync(Two, Ct)).ShouldBe(LookAvailability.NoSubject);
    }

    [Fact]
    public async Task A_newer_run_that_reaches_the_block_does_not_decide_and_the_older_clear_one_does()
    {
        Run("ORB", 3, new DateOnly(2026, 10, 1), passes: false, to: new DateOnly(2026, 11, 20)); // newest, window reaches the block
        Run("ORB", 3, new DateOnly(2026, 10, 1), passes: true);

        (await Step().RunAsync(Ct)).ShouldBe("look queued for ORB v3");
    }

    [Fact]
    public async Task A_version_beyond_a_few_hundred_other_runs_is_still_found_there_is_no_cap()
    {
        for (var i = 0; i < 600; i++)
        {
            Run("OTHER", i + 10, new DateOnly(2026, 10, 1), passes: false);
        }

        Run("ORB", 3, new DateOnly(2026, 10, 1));

        (await new StrategyLookSlot(_store, Candidates()).ReadAsync(Two, Ct)).ShouldBe(LookAvailability.Available);
    }

    // ---- the post-run auto look and the queued auto job

    [Fact]
    public async Task With_the_first_job_still_queued_no_second_candidate_is_claimed()
    {
        Run("ORB", 4, new DateOnly(2026, 10, 1));
        Run("ORB", 3, new DateOnly(2026, 9, 1));

        (await Step().RunAsync(Ct)).ShouldBe("look queued for ORB v4");
        var again = await Step().RunAsync(Ct);

        again.ShouldBe("look already queued for ORB v4");
        _jobs.Count.ShouldBe(1, "B is not queued while A's job waits: a second look would take the slot first and burn A's look");
    }

    [Fact]
    public async Task An_auto_job_whose_slot_was_taken_or_whose_version_is_not_pre_dated_stores_nothing_and_ends_cancelled()
    {
        var early = Run("ORB", 3, new DateOnly(2026, 10, 1));
        var late = Run("ORB", 4, new DateOnly(2026, 12, 15));
        var runner = new HoldOutJobRunner(_runs, Unlocker(), Substitute.For<ILabProgress>(), new HoldOutOptions(), _time, NullLogger<HoldOutJobRunner>.Instance);
        LabRun JobFor(LabRun source) => Queued(new NewLabRun(Guid.NewGuid(), HoldOutJob.KeyOf(source.Id, Two), source.StrategyId, source.Version, source.StrategyHash, source.Universe, source.From, source.To, HoldOutJob.Method, HoldOutJob.ConfigHash, source.EngineVersion, [HoldOutJob.Label(source.Id, "system:auto-unlock")], _time.GetUtcNow()));
        FreshStrategyLook(Two); // the slot was taken before the job ran
        var taken = JobFor(early);
        var notPredated = JobFor(late);

        await runner.ExecuteAsync(taken, Ct);
        _store.Unlocks.Clear();
        await runner.ExecuteAsync(notPredated, Ct);

        _store.Unlocks.ShouldBeEmpty();
        await _runs.Received(1).FinishAsync(Arg.Is<LabFinish>(f => f.Id == taken.Id && f.State == LabRunState.Cancelled && f.Problems.Any(p => p.Code == "lab.holdout_auto_refused" && p.Message.Contains("already taken", StringComparison.Ordinal))), Arg.Any<CancellationToken>());
        await _runs.Received(1).FinishAsync(Arg.Is<LabFinish>(f => f.Id == notPredated.Id && f.State == LabRunState.Cancelled && f.Problems.Any(p => p.Code == "lab.holdout_auto_refused")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_queued_auto_job_is_cancelled_when_the_auto_look_was_turned_off_before_it_ran()
    {
        var run = Run("ORB", 3, new DateOnly(2026, 10, 1));
        var job = Queued(new NewLabRun(Guid.NewGuid(), HoldOutJob.KeyOf(run.Id, Two), run.StrategyId, run.Version, run.StrategyHash, run.Universe, run.From, run.To, HoldOutJob.Method, HoldOutJob.ConfigHash, run.EngineVersion, [HoldOutJob.Label(run.Id, "system:auto-unlock")], _time.GetUtcNow()));
        var off = new HoldOutOptions(AutoUnlock: false);
        var runner = new HoldOutJobRunner(_runs, Unlocker(off), Substitute.For<ILabProgress>(), off, _time, NullLogger<HoldOutJobRunner>.Instance);

        await runner.ExecuteAsync(job, Ct);

        _store.Unlocks.ShouldBeEmpty();
        await _runs.Received(1).FinishAsync(Arg.Is<LabFinish>(f => f.Id == job.Id && f.State == LabRunState.Cancelled && f.Problems.Any(p => p.Code == "lab.holdout_auto_off" && p.Severity == "info")), Arg.Any<CancellationToken>());
    }

    // ---- the slot and the auto release

    [Fact]
    public async Task The_strategy_slot_reads_used_available_and_no_subject()
    {
        var slot = new StrategyLookSlot(_store, Candidates());
        (await slot.ReadAsync(Two, Ct)).ShouldBe(LookAvailability.NoSubject);
        Run("ORB", 3, new DateOnly(2026, 10, 1));
        (await slot.ReadAsync(Two, Ct)).ShouldBe(LookAvailability.Available);
        FreshStrategyLook(Two);

        (await slot.ReadAsync(Two, Ct)).ShouldBe(LookAvailability.Used);
    }

    [Fact]
    public async Task A_sealed_block_with_a_fresh_look_and_no_available_look_is_released_by_the_auto_option_and_recorded_as_auto()
    {
        FreshStrategyLook(Two);

        var done = await Releaser().TryAutoAsync(Ct);

        done.Released.ShouldBeTrue();
        _store.Releases.Last().ShouldSatisfyAllConditions(r => r.Mode.ShouldBe("auto"), r => r.Actor.ShouldBe("system:auto-release"), r => r.SettingHash.ShouldBe(new HoldOutOptions().SnapshotHash()));
        _store.Blocks.ShouldContain(b => b.Start == new DateOnly(2027, 5, 1));
    }

    [Fact]
    public async Task A_sealed_block_whose_strategy_slot_is_free_while_an_eligible_version_exists_is_not_released()
    {
        // a look of another kind exists, the strategy slot is free and a pre-dated version could take it
        _store.Unlocks.Add(new StoredUnlock("model-family:tf-1@2026-10-31", 1, "model", "auto", "system:ml", DateTimeOffset.UnixEpoch, Hash, false, Two, Guid.NewGuid(), View(Two) with { Kind = "model" }));
        Run("ORB", 3, new DateOnly(2026, 10, 1));

        var done = await Releaser().TryAutoAsync(Ct);

        done.ShouldBe(new AutoReleaseResult(false, "look_available"));
        _store.Releases.Count.ShouldBe(1, "only the earlier release of block 1");
    }

    [Fact]
    public async Task An_unlooked_block_and_an_unreadable_slot_are_never_released_and_off_releases_nothing()
    {
        (await Releaser().TryAutoAsync(Ct)).ShouldBe(new AutoReleaseResult(false, "unlooked"));
        FreshStrategyLook(Two);
        (await Releaser(new HoldOutOptions(AutoRelease: false)).TryAutoAsync(Ct)).ShouldBe(new AutoReleaseResult(false, "off"));
        _store.Unlocks.Clear();
        _store.Unlocks.Add(new StoredUnlock("model-family:tf-1@2026-10-31", 1, "model", "auto", "system:ml", DateTimeOffset.UnixEpoch, Hash, false, Two, Guid.NewGuid(), View(Two) with { Kind = "model" }));
        _results.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<ValueTask<LabVerdictView?>>(_ => throw new InvalidOperationException("down"));
        Run("ORB", 3, new DateOnly(2026, 10, 1));
        _results.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<ValueTask<LabVerdictView?>>(_ => throw new InvalidOperationException("down"));

        var unreadable = await Releaser().TryAutoAsync(Ct);

        unreadable.ShouldBe(new AutoReleaseResult(false, "unavailable"));
        _store.Releases.Count.ShouldBe(1);
    }

    // ---- the seal runner

    private sealed class FakeStep(string name, int order, Func<string> act) : IHoldOutSealStep
    {
        public string Name { get; } = name;

        public int Order { get; } = order;

        public ValueTask<string> RunAsync(CancellationToken cancellationToken) => ValueTask.FromResult(act());
    }

    [Fact]
    public async Task The_runner_runs_the_steps_in_order_and_one_failing_step_does_not_stop_another()
    {
        var order = new List<string>();
        var handler = new RunHoldOutEpochsHandler(
            Epochs(),
            [new FakeStep("last", 90, () => Add(order, "last")), new FakeStep("boom", 30, () => throw new InvalidOperationException("x")), new FakeStep("first", 20, () => Add(order, "first"))],
            NullLogger<RunHoldOutEpochsHandler>.Instance);

        var result = await handler.HandleAsync(new RunHoldOutEpochsCommand(), Ct);

        order.ShouldBe(["first", "last"]);
        result.Value.Failed.ShouldBe(1);
        result.Value.Summary.ShouldBe("block 2026-10-31 sealed; first: first; boom: failed; last: last");
    }

    [Fact]
    public async Task A_chain_that_cannot_be_read_fails_the_run_with_no_step_run()
    {
        _store.Failing = true;
        var ran = false;
        var handler = new RunHoldOutEpochsHandler(Epochs(), [new FakeStep("s", 1, () => Add(new List<string>(), "x", () => ran = true))], NullLogger<RunHoldOutEpochsHandler>.Instance);

        var result = await handler.HandleAsync(new RunHoldOutEpochsCommand(), Ct);

        result.IsFailure.ShouldBeTrue();
        ran.ShouldBeFalse();
    }

    [Fact]
    public async Task The_auto_release_step_reports_what_the_releaser_did()
    {
        var step = new HoldOutAutoReleaseStep(Releaser());
        (await step.RunAsync(Ct)).ShouldBe("kept: unlooked");
        FreshStrategyLook(Two);

        (await step.RunAsync(Ct)).ShouldBe("block released (auto)");
    }

    [Fact]
    public void The_service_is_enabled_at_first_start_after_eod_and_latest_only()
    {
        var service = new HoldOutEpochsService(null!);

        service.FirstStartSettings()!.Enabled.ShouldBeTrue();
        HoldOutEpochsService.Definition.Constraints.NotBeforeSessionEnd.ShouldBeTrue();
        HoldOutEpochsService.Definition.Constraints.RecoveryClass.ShouldBe(XDrishti.Domain.Platform.Services.RecoveryClass.LatestOnly);
        HoldOutEpochsService.Definition.Constraints.MustFollow.ShouldBe(["eod"]);
    }

    // ---- the stalled-block notice

    [Fact]
    public async Task A_block_sealed_for_twenty_sessions_without_a_release_gets_one_notice_naming_the_reason()
    {
        var notices = new HoldOutStalledNotices(Epochs(), Releaser());
        var after = new FakeTimeProvider(new DateTimeOffset(2027, 6, 1, 6, 0, 0, TimeSpan.Zero)); // 22 sessions after 2027-04-30
        var stalled = new HoldOutStalledNotices(Epochs(after), new HoldOutReleaser(Epochs(after), _store, [new StrategyLookSlot(_store, Candidates())], new HoldOutOptions(), after, _events, _unitOfWork, NullLogger<HoldOutReleaser>.Instance));

        (await notices.ListAsync(_time.GetUtcNow(), Ct)).ShouldBeEmpty("only three sessions have passed");
        var list = await stalled.ListAsync(after.GetUtcNow(), Ct);

        var notice = list.ShouldHaveSingleItem();
        notice.Kind.ShouldBe("holdout_stalled");
        notice.Id.ShouldBe("holdout:stalled:2026-10-31:unlooked");
        notice.Text.ShouldContain("2026-10-31");
        notice.Text.ShouldContain("forfeit");
        notice.Route.ShouldBe("/lab");
    }

    [Fact]
    public void The_stall_rule_is_twenty_sessions()
    {
        HoldOutStall.IsStalled(19).ShouldBeFalse();
        HoldOutStall.IsStalled(20).ShouldBeTrue();
    }

    private static string Add(List<string> list, string word, Action? also = null)
    {
        also?.Invoke();
        list.Add(word);
        return word;
    }
}
