#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Application.Replay.Reads;
using XDrishti.Application.Replay.Sweeps;
using XDrishti.Application.Tests.Replay.Job;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Sweeps;

/// <summary>SR-02, AC 3 to 5: the baseline request and the sweep request become runs, a bad sweep names its bad starts, a sweep is refused while a replay is active, and a cancel stops the whole sweep.</summary>
public sealed class ReplaySweepHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Last = new(2026, 6, 30);

    private sealed class Rig(bool running = false)
    {
        public IReplayFacts Facts { get; } = Setup(Substitute.For<IReplayFacts>());

        public IReplayReads Reads { get; } = RunningReads(running);

        public IAutoScopeStore Scope { get; } = Substitute.For<IAutoScopeStore>();

        public IRiskProfileStore Profiles { get; } = Profile(Substitute.For<IRiskProfileStore>());

        public ICommandHandler<StartReplayCommand, StartedReplay> Start { get; } = Substitute.For<ICommandHandler<StartReplayCommand, StartedReplay>>();

        public StartReplayCommand? Sent { get; set; }

        private static IReplayFacts Setup(IReplayFacts facts)
        {
            facts.LastReadableSessionAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Last));
            return facts;
        }

        private static IReplayReads RunningReads(bool running)
        {
            var reads = Substitute.For<IReplayReads>();
            reads.AnyRunningAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(running));
            return reads;
        }

        private static IRiskProfileStore Profile(IRiskProfileStore store)
        {
            store.ListAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<XDrishti.Domain.Accounts.RiskProfile>>([]));
            return store;
        }

        public ValueTask<Result<StartedReplay>> Sweep(params DateOnly[] starts)
        {
            Start.HandleAsync(Arg.Do<StartReplayCommand>(c => Sent = c), Arg.Any<CancellationToken>())
                .Returns(c => ValueTask.FromResult(Result.Success(new StartedReplay([.. ((StartReplayCommand)c[0]).Runs.Select(_ => Guid.NewGuid())], null, 1, Guid.NewGuid()))));
            return new StartReplaySweepRequestHandler(Facts, Reads, Scope, Profiles, Start).HandleAsync(new StartReplaySweepRequest(starts, null, [Guid.NewGuid()], null, null, null, null, null, false, false), Ct);
        }
    }

    [Fact]
    public async Task A_sweep_becomes_one_frozen_selection_run_per_start_in_date_order_with_one_shared_end()
    {
        var rig = new Rig();

        var result = await rig.Sweep(new DateOnly(2025, 4, 1), new DateOnly(2025, 1, 1), new DateOnly(2025, 7, 1));

        result.IsSuccess.ShouldBeTrue();
        rig.Sent!.SweepStarts.ShouldBe([new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1), new DateOnly(2025, 7, 1)]);
        rig.Sent.Runs.Select(r => r.From).ShouldBe(rig.Sent.SweepStarts!);
        rig.Sent.Runs.ShouldAllBe(r => r.To == Last && r.Mode == ReplayMode.Frozen && r.Arm == ReplayArm.Selection);
        result.Value.SweepId.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_thirteenth_start_or_a_single_one_is_replay_invalid_and_nothing_is_started()
    {
        var rig = new Rig();

        var many = await rig.Sweep([.. Enumerable.Range(1, 13).Select(i => new DateOnly(2024, 1, 1).AddDays(i * 10))]);
        var one = await rig.Sweep(new DateOnly(2025, 1, 1));

        many.Error.Code.ShouldBe("replay.invalid");
        one.Error.Code.ShouldBe("replay.invalid");
        rig.Sent.ShouldBeNull();
    }

    [Fact]
    public async Task A_sweep_while_a_replay_is_active_is_busy()
    {
        var rig = new Rig(running: true);

        var result = await rig.Sweep(new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1));

        result.Error.Code.ShouldBe(StartReplayHandler.BusyCode);
        rig.Sent.ShouldBeNull();
    }

    [Fact]
    public async Task A_baseline_request_starts_the_selection_run_and_the_baseline_run_in_frozen_mode()
    {
        var rig = new Rig();
        rig.Start.HandleAsync(Arg.Do<StartReplayCommand>(c => rig.Sent = c), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new StartedReplay([Guid.NewGuid(), Guid.NewGuid()], Guid.NewGuid(), 1))));
        var handler = new StartReplayRequestHandler(rig.Facts, rig.Reads, rig.Scope, rig.Profiles, rig.Start);

        var result = await handler.HandleAsync(new StartReplayRequest(new DateOnly(2025, 1, 6), null, [Guid.NewGuid()], null, null, null, null, null, null, false, false, Baseline: true), Ct);

        result.IsSuccess.ShouldBeTrue();
        rig.Sent!.Runs.Select(r => (r.Mode, r.Arm)).ShouldBe([(ReplayMode.Frozen, ReplayArm.Selection), (ReplayMode.Frozen, ReplayArm.Baseline)]);
        rig.Sent.Runs[1].ShouldBe(rig.Sent.Runs[0] with { Arm = ReplayArm.Baseline });
    }

    [Fact]
    public async Task A_baseline_with_the_learning_mode_is_replay_invalid()
    {
        var rig = new Rig();
        var handler = new StartReplayRequestHandler(rig.Facts, rig.Reads, rig.Scope, rig.Profiles, rig.Start);

        var result = await handler.HandleAsync(new StartReplayRequest(new DateOnly(2025, 1, 6), null, [Guid.NewGuid()], null, null, null, null, null, ["frozen", "learning"], false, false, Baseline: true), Ct);

        result.Error.Code.ShouldBe("replay.invalid");
        rig.Sent.ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_a_sweep_stops_the_run_in_flight_and_cancels_every_queued_one_and_keeps_the_rest()
    {
        var store = new FakeReplayStore();
        var sweep = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 10, 1, 0, 0, TimeSpan.Zero);
        NewReplayRun Run(int i, Guid? sweepId) => new(Guid.NewGuid(), new DateOnly(2025, 1, 1).AddDays(i), Last, "frozen", "{}", "{}", "{}", null, new string('a', 64), new string('b', 64), 5, at.AddTicks(i * 10), "selection", sweepId);
        var runs = new[] { Run(0, sweep), Run(1, sweep), Run(2, sweep), Run(3, null) };
        await store.InsertAsync(null, runs, Ct, new NewReplaySweep(sweep, "{}", [new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2)], at));
        store.Of(runs[0].Id).State = ReplayRunStates.Running;
        var handler = new CancelReplaySweepHandler(store, store, new FakeTimeProvider(at));

        var result = await handler.HandleAsync(new CancelReplaySweepCommand(sweep), Ct);

        result.Value.ShouldBe(3);
        store.Runs.Select(r => r.State).ShouldBe([ReplayRunStates.Cancelling, ReplayRunStates.Cancelled, ReplayRunStates.Cancelled, ReplayRunStates.Queued]);
        (await handler.HandleAsync(new CancelReplaySweepCommand(Guid.NewGuid()), Ct)).Error.Code.ShouldBe("replay.sweep_not_found");
    }

    [Fact]
    public async Task A_finished_sweep_has_nothing_to_cancel()
    {
        var store = new FakeReplayStore();
        var sweep = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 10, 1, 0, 0, TimeSpan.Zero);
        await store.InsertAsync(null, [new NewReplayRun(Guid.NewGuid(), new DateOnly(2025, 1, 1), Last, "frozen", "{}", "{}", "{}", null, new string('a', 64), new string('b', 64), 5, at, "selection", sweep)], Ct, new NewReplaySweep(sweep, "{}", [new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1)], at));
        store.Runs[0].State = ReplayRunStates.Completed;

        var result = await new CancelReplaySweepHandler(store, store, new FakeTimeProvider(at)).HandleAsync(new CancelReplaySweepCommand(sweep), Ct);

        result.Error.Code.ShouldBe("replay.not_active");
    }

    [Fact]
    public async Task The_sweep_read_gives_the_rows_in_date_order_the_state_and_the_spread_of_the_completed_starts()
    {
        var store = new FakeReplayStore();
        var sweep = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 10, 1, 0, 0, TimeSpan.Zero);
        var p = ReplayJson.Canonical(new ReplayParams(new DateOnly(2025, 1, 1), Last, new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen));
        NewReplayRun Run(int i) => new(Guid.NewGuid(), new DateOnly(2025, 1, 1).AddMonths(i * 3), Last, "frozen", p, "{}", "{}", null, new string('a', 64), new string('b', 64), 5, at.AddTicks(i * 10), "selection", sweep);
        var created = new[] { Run(0), Run(1), Run(2) };
        await store.InsertAsync(null, created, Ct, new NewReplaySweep(sweep, p, [.. created.Select(c => c.From)], at));
        store.Of(created[0].Id).State = ReplayRunStates.Completed;
        store.Of(created[1].Id).State = ReplayRunStates.Completed;
        store.Of(created[2].Id).State = ReplayRunStates.Running;
        var cagr = new Dictionary<Guid, double> { [created[0].Id] = 0.10, [created[1].Id] = -0.02 };
        var runs = Substitute.For<IQueryHandler<GetReplayRunQuery, Result<ReplayRunView>>>();
        runs.HandleAsync(Arg.Any<GetReplayRunQuery>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var id = ((GetReplayRunQuery)c[0]).RunId;
            var figures = cagr.TryGetValue(id, out var g) ? new ReplayFigures(1_000_000m, 1_100_000m, 10m, g, 4m, 1.0, 0.5, 20, 18, 0.5, 0.2) : null;
            return ValueTask.FromResult(Result.Success(new ReplayRunView([], id, null, at, null, default, default, "frozen", "x", null, new ReplayCompleteness(false, 0, 5, null), "gross", "gross", 1, "h", null, default, default, figures, [])));
        });
        var equity = Substitute.For<IQueryHandler<GetReplayEquityQuery, Result<ReplayEquityView>>>();
        equity.HandleAsync(Arg.Any<GetReplayEquityQuery>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var id = ((GetReplayEquityQuery)c[0]).RunId;
            return ValueTask.FromResult(Result.Success(new ReplayEquityView([], id, new ReplayCompleteness(true, 5, 5, null), "gross", 1_000_000m, "N500", null, true, [new ReplayEquityPoint(Last, 1_100_000m, 0m, 0m, id == created[0].Id ? 1_050_000m : 1_150_000m)])));
        });

        var result = await new GetReplaySweepHandler(store, runs, equity).HandleAsync(new GetReplaySweepQuery(sweep), Ct);

        var view = result.Value;
        view.State.ShouldBe("running");
        view.Complete.ShouldBeFalse();
        (view.LooksAdded, view.CompletedStarts).ShouldBe((3, 2));
        view.Starts.Select(s => s.From).ShouldBe([.. created.Select(c => c.From)]);
        view.Starts.Select(s => s.BeatsBenchmark).ShouldBe([true, false, null]);
        view.Spread.Cagr!.Min.ShouldBe(-0.02);
        view.Spread.Cagr.Max.ShouldBe(0.10);
        view.Spread.ShareCagrPositive!.Value.ShouldBe(0.5);
        (await new GetReplaySweepHandler(store, runs, equity).HandleAsync(new GetReplaySweepQuery(Guid.NewGuid()), Ct)).Error.Code.ShouldBe("replay.sweep_not_found");
    }

    [Fact]
    public async Task The_start_handler_names_every_start_with_too_few_sessions_and_stores_nothing()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());
        var short1 = ReplayRig.Params(6);
        var short2 = ReplayRig.Params(5);
        var rigRunner = new StartReplayHandler(ReplayRig.Create(rig.World).Driver, new ReplayInputHasher(ReplayRig.Create(rig.World).Facts, ReplayRig.Create(rig.World).Facts), ReplayRig.Create(rig.World).Facts, rig.Gate, rig.StartOptions, rig.Store, rig.Ledger, rig.Time, NullLogger<StartReplayHandler>.Instance);

        var result = await rigRunner.HandleAsync(new StartReplayCommand([short1, short2 with { From = short1.From.AddDays(1) }], [short1.From, short1.From.AddDays(1)]), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("replay.invalid");
        result.Error.Message.ShouldContain("only");
        result.Error.Message.ShouldContain("126");
        rig.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_single_replay_started_while_a_sweep_has_queued_runs_is_busy()
    {
        var rig = new ReplayJobRig(ReplayJobRig.SwingWorld());
        var sweep = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 10, 1, 0, 0, TimeSpan.Zero);
        await rig.Store.InsertAsync(null, [new NewReplayRun(Guid.NewGuid(), new DateOnly(2025, 1, 1), Last, "frozen", "{}", "{}", "{}", null, new string('a', 64), new string('b', 64), 5, at, "selection", sweep)], Ct, new NewReplaySweep(sweep, "{}", [new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1)], at));

        var result = await rig.StartAsync(ReplayRig.Params(6));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(StartReplayHandler.BusyCode);
    }
}
