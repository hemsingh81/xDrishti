#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Lab;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Sim;
using XDrishti.Application.Tests.Lab;
using XDrishti.Application.Tests.SimAll;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Replay.Sim;

/// <summary>SR-04 AC 1 and 2: a start is stored with its input hash, settings snapshot and cursor, recorded as a look; a start that cannot be used is refused and, when it reaches hold-out block 1, claims nothing.</summary>
public sealed class StartSimDaysHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SimDaysRig _rig;
    private readonly MemoryHoldOutStore _holdOut = new();
    private readonly ILabRunStore _runs = Substitute.For<ILabRunStore>();
    private readonly FakeLedger _ledger = new();

    public StartSimDaysHandlerTests()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB");
        world.Rising("AAA", 0.7);
        world.Rising("BBB", 2.0);
        _rig = new SimDaysRig(world);
        _holdOut.Blocks.Add(new HoldOutBlock(world.HoldOutStart, new DateOnly(2024, 1, 1), world.HoldOutStart.AddMonths(6), 6, new string('h', 64), DateTimeOffset.UnixEpoch));
        _runs.ListAsync(Arg.Any<string?>(), Arg.Any<LabRunState?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<LabRun>)[]);
        _runs.ListSucceededFullTestsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<LabRun>)[]);
    }

    private sealed class NoPass : IHoldOutPass
    {
        public ValueTask<Domain.Common.Result<HoldOutFigures>> RunAsync(LabRun run, LabVerdictView verdict, HoldOutBlock block, CancellationToken cancellationToken) => throw new InvalidOperationException("a start never runs a look");
    }

    private sealed class FailingLedger(ITestLedger inner) : ITestLedger
    {
        public bool Fail { get; set; } = true;

        public ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken) => Fail ? throw new InvalidOperationException("ledger down") : inner.AppendAsync(row, cancellationToken);

        public ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken) => inner.CountVariantsAsync(strategyId, cancellationToken);

        public ValueTask<TrialCounts> CountTrialsAsync(string strategyId, CancellationToken cancellationToken) => inner.CountTrialsAsync(strategyId, cancellationToken);

        public ValueTask<bool> HasFullRunAsync(string strategyId, string strategyHash, CancellationToken cancellationToken) => inner.HasFullRunAsync(strategyId, strategyHash, cancellationToken);

        public ValueTask<bool> HasHashAsync(string strategyId, string strategyHash, CancellationToken cancellationToken) => inner.HasHashAsync(strategyId, strategyHash, cancellationToken);

        public ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken) => inner.ListAsync(strategyId, cancellationToken);
    }

    private SimDaysHoldOut? _simHoldOut;

    private StartSimDaysHandler Handler(HoldOutOptions? holdOutOptions = null)
    {
        var o = holdOutOptions ?? new HoldOutOptions(TrainingEmbargoDays: _rig.World.EmbargoDays);
        var rig = ReplayRig.Create(_rig.World);
        var bars = Substitute.For<IBarQueries>();
        bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(2, 1000, new DateTimeOffset(ReplayWorld.Sessions[0].ToDateTime(new TimeOnly(9, 15)), TimeSpan.FromHours(5.5)), new DateTimeOffset(2026, 11, 6, 15, 29, 0, TimeSpan.FromHours(5.5)), 0, 0));
        var calendar = new FixedCalendar();
        var epochs = new HoldOutEpochs(_holdOut, calendar, _rig.Time);
        var guard = new LabHoldOutGuard(_holdOut, bars, o, _rig.Time);
        var freezes = Substitute.For<IPlanFreezeStore>();
        var results = Substitute.For<ILabResultStore>();
        var unlocker = new HoldOutUnlocker(_runs, results, _holdOut, freezes, new NoPass(), guard, epochs, o, _rig.Time, Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), NullLogger<HoldOutUnlocker>.Instance);
        var step = new StrategyAutoLookStep(epochs, new HoldOutStrategyCandidates(_runs, results, freezes, _holdOut), unlocker, _holdOut, _runs, o, _rig.Time, NullLogger<StrategyAutoLookStep>.Instance);
        var simHoldOut = _simHoldOut = new SimDaysHoldOut(epochs, _holdOut, step, _runs, guard, o, calendar, _rig.Time);
        var profiles = Substitute.For<IRiskProfileStore>();
        profiles.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Domain.Accounts.RiskProfile>)[]);
        return new StartSimDaysHandler(rig.Driver, rig.Facts, new ReplayInputHasher(rig.Facts, rig.Facts), rig.Facts, simHoldOut, profiles, new SystemReplayOptions(), _rig.Options, _rig.Store, _ledger, _rig.Time, NullLogger<StartSimDaysHandler>.Instance);
    }

    private static StartSimDaysCommand Command(DateOnly start, DateOnly? end = null, decimal? capital = null, bool emptyScope = false) =>
        new(start, end, emptyScope ? [] : [ReplayWorld.Basket], null, capital, null, null);

    [Fact]
    public async Task A_start_is_stored_with_its_input_hash_and_settings_snapshot_its_cursor_on_the_first_session_and_a_look_in_the_ledger()
    {
        var saturday = ReplayWorld.Start.AddDays(-2);

        var started = await Handler().HandleAsync(Command(saturday, ReplayWorld.RunSessions[4]), Ct);

        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Error.Message : string.Empty);
        var row = _rig.Store.Row(started.Value);
        row.InputHash.ShouldMatch("^[0-9a-f]{64}$");
        ReplayJson.Read<ReplaySettingsSnapshot>(row.SettingsJson).ShouldNotBeNull();
        (row.Cursor, row.Phase, row.Mode, row.Capital, row.End).ShouldBe((ReplayWorld.Start, SimDays.Idle, "learning", 1_000_000m, ReplayWorld.RunSessions[4]));
        _ledger.Rows.ShouldNotBeEmpty();
        _ledger.Rows.ShouldAllBe(r => r.TrialKind == LedgerRow.SystemReplayKind && r.RunKey == row.InputHash);
    }

    [Fact]
    public async Task Start_after_end_an_empty_scope_a_capital_of_zero_and_a_start_before_the_warm_up_are_sim_invalid()
    {
        var handler = Handler();

        var afterEnd = await handler.HandleAsync(Command(ReplayWorld.RunSessions[4], ReplayWorld.RunSessions[1]), Ct);
        var empty = await handler.HandleAsync(Command(ReplayWorld.Start, emptyScope: true), Ct);
        var zero = await handler.HandleAsync(Command(ReplayWorld.Start, capital: 0), Ct);
        var early = await handler.HandleAsync(Command(ReplayWorld.Sessions[3], ReplayWorld.RunSessions[1]), Ct);

        new[] { afterEnd, empty, zero, early }.ShouldAllBe(r => r.IsFailure && r.Error.Code == SimDays.InvalidCode);
        early.Error.Message.ShouldContain("warm-up");
        (await _rig.Store.ActiveAsync(Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_second_active_simulation_is_sim_busy()
    {
        (await Handler().HandleAsync(Command(ReplayWorld.Start, ReplayWorld.RunSessions[2]), Ct)).IsSuccess.ShouldBeTrue();

        var second = await Handler().HandleAsync(Command(ReplayWorld.Start, ReplayWorld.RunSessions[2]), Ct);

        second.Error.Code.ShouldBe(SimDays.BusyCode);
    }

    [Fact]
    public async Task A_refused_start_that_reaches_block_one_claims_nothing()
    {
        _rig.World.HoldOutStart = new DateOnly(2026, 10, 8);
        _rig.World.EmbargoDays = 0;
        _holdOut.Blocks.Clear();
        _holdOut.Blocks.Add(new HoldOutBlock(_rig.World.HoldOutStart, new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 30), 6, new string('h', 64), DateTimeOffset.UnixEpoch)); // sealed, its look free

        var refused = await Handler().HandleAsync(Command(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 30)), Ct); // the whole range is inside the block

        refused.Error.Code.ShouldBe(SimDays.InvalidCode);
        refused.Error.Message.ShouldContain("look is free", customMessage: "the decision found the look to claim, and the refusal came first");
        await _runs.DidNotReceive().InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>());
        await _runs.DidNotReceive().ListSucceededFullTestsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_continue_whose_ledger_rows_fail_extends_nothing_and_a_retry_records_the_look_once()
    {
        var id = (await Handler().HandleAsync(Command(ReplayWorld.Start, ReplayWorld.RunSessions[2]), Ct)).Value;
        (await new SetSimAutoHandler(_rig.Store, _rig.Calendar, _rig.Options, _rig.Time).HandleAsync(new SetSimAutoCommand(id, true), Ct)).IsSuccess.ShouldBeTrue();
        await _rig.Runner().TickAsync(Ct);
        _rig.Store.Row(id).Phase.ShouldBe(SimDays.Complete);
        var ledger = new FailingLedger(_ledger);
        var before = _ledger.Rows.Count;
        var handler = new ContinueSimDaysHandler(_rig.Store, _simHoldOut!, _rig.Calendar, ledger, _rig.Options, _rig.Time, NullLogger<ContinueSimDaysHandler>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(async () => await handler.HandleAsync(new ContinueSimDaysCommand(id), Ct));
        (_rig.Store.Row(id).Phase, _rig.Store.Row(id).End).ShouldBe((SimDays.Complete, ReplayWorld.RunSessions[2]), "no extension without its look");
        ledger.Fail = false;
        var continued = await handler.HandleAsync(new ContinueSimDaysCommand(id), Ct);
        var added = _ledger.Rows.Count - before;
        (await handler.HandleAsync(new ContinueSimDaysCommand(id), Ct)).IsFailure.ShouldBeTrue("not complete any more");

        continued.IsSuccess.ShouldBeTrue(continued.IsFailure ? continued.Error.Message : string.Empty);
        (continued.Value.Phase, continued.Value.Cursor).ShouldBe((SimDays.Idle, ReplayWorld.RunSessions[3]));
        added.ShouldBeGreaterThan(0);
        _ledger.Rows.Count.ShouldBe(before + added);
    }
}
