using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Learning.CellsRefresh;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Learning.CellsRefresh;

/// <summary>The scheduled refresh (spec LC-01, ACs 4 to 6 and 11): kept and extended sources, the ledger row, the fail-closed paths and the all-or-nothing rule, over fakes.</summary>
public sealed class RefreshCellStatsHandlerTests
{
    private static readonly string R1 = new('a', 64);

    private static readonly string R2 = new('b', 64);

    private static readonly string R1New = new('c', 64);

    private static readonly string R3 = new('d', 64);

    private static readonly string Sha1 = new('1', 64);

    private static readonly string Sha2 = new('2', 64);

    private static readonly string Sha3 = new('3', 64);

    private static readonly DateOnly Session = new(2026, 11, 12);

    private static readonly DateOnly End = new(2026, 4, 4);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ICellStatsStore _cells = Substitute.For<ICellStatsStore>();

    private readonly IHoldOutStore _holdOut = Substitute.For<IHoldOutStore>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly IRegistrationStore _registrations = Substitute.For<IRegistrationStore>();

    private readonly ISourceRequestPlanner _planner = Substitute.For<ISourceRequestPlanner>();

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private readonly ICommandHandler<StoreRunCommand, StoreOutcome> _store = Substitute.For<ICommandHandler<StoreRunCommand, StoreOutcome>>();

    private readonly IRunRecordWriter _writer = Substitute.For<IRunRecordWriter>();

    private readonly ITestLedger _ledger = Substitute.For<ITestLedger>();

    private readonly ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome> _compute = Substitute.For<ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>>();

    private ComputeCellStatsCommand? _computed;

    public RefreshCellStatsHandlerTests()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _holdOut.CurrentAsync(Ct).Returns(new HoldOutBlock(new DateOnly(2026, 5, 5), new DateOnly(2020, 1, 1), new DateOnly(2026, 11, 1), 6, "h", DateTimeOffset.UnixEpoch));
        _cells.ListRegimeLatestAsync(null, null, Ct).Returns([]);
        _cells.ListUniverseLatestAsync(null, null, Ct).Returns([]);
        _compute.HandleAsync(Arg.Do<ComputeCellStatsCommand>(c => _computed = c), Ct).Returns(Result.Success(new CellStatsOutcome(new string('9', 64), 2, 12, 80, false)));
        _writer.Write(Arg.Any<SimulationRun>()).Returns(_ => (RunRecordText)RuntimeHelpers.GetUninitializedObject(typeof(RunRecordText)));
        _store.HandleAsync(Arg.Any<StoreRunCommand>(), Ct).Returns(Result.Success(new StoreOutcome(1, false)));
        _planner.PlanAsync(Arg.Any<StoredRunHeader>(), Arg.Any<string>(), Ct).Returns(call => Result.Success(Request(call.Arg<StoredRunHeader>().To)));
    }

    private RefreshCellStatsHandler Handler() =>
        new(_runs, _cells, _holdOut, new HoldOutOptions(), _calendar, _strategies, _registrations, _planner, _simulation, _store, _writer, _ledger, _compute,
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)), NullLogger<RefreshCellStatsHandler>.Instance);

    private static SimulationRequest Request(DateOnly to) =>
        new("ORB", null, "15m", new DateOnly(2024, 11, 1), to, [Guid.NewGuid()], null, CostChoice.Gross);

    private static StoredRunHeader Header(string key, string sha, string strategy, DateOnly to) =>
        new(1, key, "sim", 8, 5, strategy, sha, new DateOnly(2024, 11, 1), to, "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static StoredCellStats Template(params CellSource[] sources)
    {
        var cell = new CellId("ORB", "15m", "BUY", "intraday", "E");
        var row = new CellStatsRow(cell, CellWindow.All, new DateOnly(2026, 11, 5), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new StoredCellStats(new string('8', 64), DateTimeOffset.UnixEpoch, new CellStatsOptions(RecencyHalfLifeDays: 90, PriorStrength: 10, MinTrades: 20), string.Join(',', sources.Select(s => $"{s.RunKey}:{s.Style}:{s.Exit}")), row);
    }

    private void Newest(params CellSource[] sources) => _cells.ListLatestCashAsync(null, null, Ct).Returns([Template(sources)]);

    private void Source(string key, string sha, string strategy, DateOnly to, bool registered)
    {
        _runs.FindAsync(key, Ct).Returns(Header(key, sha, strategy, to));
        _runs.ReadRecordTextAsync(key, Ct).Returns("{}");
        _strategies.GetByHashAsync(sha, Ct).Returns(new StoredVersion(strategy, 1, 1, sha, "src", "{}", null, null, DateTimeOffset.UnixEpoch));
        _registrations.GetAsync(strategy, 1, Ct).Returns(registered ? new StoredRegistration(strategy, 1, new Registration("a", "b", "c", "d", "e"), "t", DateTimeOffset.UnixEpoch, "o") : null);
    }

    private static SimulationRun FakeRun(string key, string sha, DateOnly to)
    {
        var raw = (SimulationRun)RuntimeHelpers.GetUninitializedObject(typeof(SimulationRun));
        var strategy = new StrategyRecord("ORB", new Dictionary<string, double>(), "{}", sha, Timeframe.M15, [Side.Buy]);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, "list", []);
        return raw with { RunKey = key, Strategy = strategy, Universe = universe, From = new DateOnly(2024, 11, 1), To = to };
    }

    [Fact]
    public async Task Sources_that_are_up_to_date_or_not_registered_are_kept_and_nothing_is_simulated_stored_or_counted()
    {
        Newest(new CellSource(R1, "intraday", "E"), new CellSource(R2, "intraday", "E2"));
        Source(R1, Sha1, "ORB", End, registered: true);
        Source(R2, Sha2, "VWAP", new DateOnly(2026, 1, 1), registered: false);

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Kept.ShouldBe([$"{R1[..12]} kept: up_to_date", $"{R2[..12]} kept: not_registered"]);
        _computed!.AsOf.ShouldBe(Session);
        _computed.Sources.ShouldBe([new CellSource(R1, "intraday", "E"), new CellSource(R2, "intraday", "E2")]);
        _computed.Options.ShouldBe(new CellStatsOptions(RecencyHalfLifeDays: 90, PriorStrength: 10, MinTrades: 20));
        _computed.Actor.ShouldBe("system:cells-refresh");
        await _simulation.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, Ct);
    }

    [Fact]
    public async Task Every_message_states_the_data_through_date_and_the_hold_out_sentence()
    {
        Newest(new CellSource(R1, "intraday", "E"));
        Source(R1, Sha1, "ORB", End, registered: true);

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.Value.Message.ShouldContain("data through 2026-04-04; no new trades can enter until a hold-out block is released (HO-01)");
    }

    [Fact]
    public async Task A_registered_source_that_ends_early_is_simulated_again_with_only_its_end_moved_and_counted_once()
    {
        Newest(new CellSource(R1, "intraday", "E"), new CellSource(R2, "intraday", "E2"));
        Source(R1, Sha1, "ORB", new DateOnly(2026, 3, 31), registered: true);
        Source(R2, Sha2, "VWAP", new DateOnly(2026, 1, 1), registered: false);
        SimulationRequest? asked = null;
        _simulation.HandleAsync(Arg.Do<RunSimulationQuery>(q => asked = q.Request), Ct).Returns(Result.Success(FakeRun(R1New, Sha1, End)));

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.IsSuccess.ShouldBeTrue();
        asked!.To.ShouldBe(End);
        asked.From.ShouldBe(new DateOnly(2024, 11, 1));
        asked.Purpose.ShouldBe(RunPurpose.Research);
        _computed!.Sources.Select(s => s.RunKey).ShouldBe([R1New, R2]);
        await _ledger.Received(1).AppendAsync(Arg.Is<LedgerRow>(r => r.TrialKind == "cells_refresh" && r.RunKey == R1New && r.StrategyId == "ORB" && r.Version == 1 && r.StrategyHash == Sha1 && r.To == End), Ct);
        result.Value.NewRuns.ShouldBe(1);
    }

    [Fact]
    public async Task A_refresh_run_that_was_stored_already_adds_no_ledger_row()
    {
        Newest(new CellSource(R1, "intraday", "E"));
        Source(R1, Sha1, "ORB", new DateOnly(2026, 3, 31), registered: true);
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Success(FakeRun(R1New, Sha1, End)));
        _store.HandleAsync(Arg.Any<StoreRunCommand>(), Ct).Returns(Result.Success(new StoreOutcome(1, true)));

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, Ct);
    }

    [Fact]
    public async Task One_failed_simulation_ends_the_run_with_its_code_and_stores_no_run_no_row_and_no_snapshot()
    {
        Newest(new CellSource(R1, "intraday", "E"), new CellSource(R3, "intraday", "E3"));
        Source(R1, Sha1, "ORB", new DateOnly(2026, 3, 31), registered: true);
        Source(R3, Sha3, "EMA", new DateOnly(2026, 3, 31), registered: true);
        _simulation.HandleAsync(Arg.Is<RunSimulationQuery>(q => q.Request.To == End), Ct).Returns(Result.Success(FakeRun(R1New, Sha1, End)), Result.Failure<SimulationRun>(Error.Conflict("sim.bars_missing", "no bars")));

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CellsRefreshErrors.SourceFailedCode);
        result.Error.Message.ShouldContain("sim.bars_missing");
        result.Error.Message.ShouldContain("(HO-01)");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, Ct);
        await _compute.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_failed_compute_ends_the_run_with_its_code()
    {
        Newest(new CellSource(R1, "intraday", "E"));
        Source(R1, Sha1, "ORB", End, registered: true);
        _compute.HandleAsync(Arg.Any<ComputeCellStatsCommand>(), Ct).Returns(Result.Failure<CellStatsOutcome>(LearningErrors.RunNotFound(R1)));

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    [Fact]
    public async Task With_no_snapshot_it_fails_closed_naming_the_first_snapshot_as_the_owners_act()
    {
        _cells.ListLatestCashAsync(null, null, Ct).Returns([]);

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.Error.Code.ShouldBe("cells_refresh.no_snapshot");
        result.Error.Message.ShouldContain("xd learn cells");
    }

    [Fact]
    public async Task With_no_hold_out_block_it_fails_closed_and_reads_no_run()
    {
        Newest(new CellSource(R1, "intraday", "E"));
        _holdOut.CurrentAsync(Ct).Returns((HoldOutBlock?)null);

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.Error.Code.ShouldBe("cells_refresh.no_holdout");
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
        await _compute.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_source_that_cannot_be_rebuilt_fails_the_run_instead_of_being_guessed()
    {
        Newest(new CellSource(R1, "intraday", "E"));
        Source(R1, Sha1, "ORB", new DateOnly(2026, 3, 31), registered: true);
        _planner.PlanAsync(Arg.Any<StoredRunHeader>(), Arg.Any<string>(), Ct).Returns(Result.Failure<SimulationRequest>(Error.Conflict("x.y", "the definition changed")));

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.Error.Code.ShouldBe(CellsRefreshErrors.NotRebuildableCode);
        await _simulation.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task The_regime_split_of_the_template_snapshot_is_repeated()
    {
        Newest(new CellSource(R1, "intraday", "E"));
        Source(R1, Sha1, "ORB", End, registered: true);
        _cells.ListRegimeLatestAsync(null, null, Ct).Returns([Template(new CellSource(R1, "intraday", "E"))]);

        await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        _computed!.ByRegime.ShouldBeTrue();
        _computed.ByUniverse.ShouldBeFalse();
    }
}
