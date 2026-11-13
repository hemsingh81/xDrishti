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
using XDrishti.Tests.Shared;

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

    private readonly IHoldOutStore _holdOut = TestHoldOutStores.NoReleases();

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

    private RefreshCellStatsHandler Handler(ITestLedger? ledger = null) =>
        new(_runs, _cells, _holdOut, new HoldOutOptions(), _calendar, _strategies, _registrations, _planner, _simulation, _store, _writer, ledger ?? _ledger, _compute,
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
    public async Task A_refresh_run_that_was_stored_already_still_appends_the_idempotent_ledger_row_and_counts_no_new_run()
    {
        Newest(new CellSource(R1, "intraday", "E"));
        Source(R1, Sha1, "ORB", new DateOnly(2026, 3, 31), registered: true);
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Success(FakeRun(R1New, Sha1, End)));
        _store.HandleAsync(Arg.Any<StoreRunCommand>(), Ct).Returns(Result.Success(new StoreOutcome(1, true)));

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NewRuns.ShouldBe(0);
        await _ledger.Received(1).AppendAsync(Arg.Is<LedgerRow>(r => r.RunKey == R1New && r.TrialKind == "cells_refresh"), Ct);
    }

    [Fact]
    public async Task A_ledger_append_that_throws_after_the_store_is_counted_once_by_the_retry()
    {
        Newest(new CellSource(R1, "intraday", "E"));
        Source(R1, Sha1, "ORB", new DateOnly(2026, 3, 31), registered: true);
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Success(FakeRun(R1New, Sha1, End)));
        var flaky = new FlakyLedger();

        await Should.ThrowAsync<InvalidOperationException>(async () => await Handler(flaky).HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct));
        _store.HandleAsync(Arg.Any<StoreRunCommand>(), Ct).Returns(Result.Success(new StoreOutcome(1, true))); // the retry finds the run stored already
        await Handler(flaky).HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);
        await Handler(flaky).HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        flaky.Rows.Count.ShouldBe(1); // one look, however often it is appended
    }

    [Fact]
    public async Task A_store_failure_after_an_earlier_source_was_stored_ends_the_run_with_no_snapshot()
    {
        Newest(new CellSource(R1, "intraday", "E"), new CellSource(R3, "intraday", "E3"));
        Source(R1, Sha1, "ORB", new DateOnly(2026, 3, 31), registered: true);
        Source(R3, Sha3, "EMA", new DateOnly(2026, 3, 31), registered: true);
        var second = new string('e', 64);
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Success(FakeRun(R1New, Sha1, End)), Result.Success(FakeRun(second, Sha3, End)));
        _store.HandleAsync(Arg.Is<StoreRunCommand>(c => c.Run!.RunKey == second), Ct).Returns(Result.Failure<StoreOutcome>(Error.Conflict("store.write_failed", "no")));

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.Error.Code.ShouldBe(CellsRefreshErrors.SourceFailedCode);
        await _compute.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_newest_snapshot_naming_more_than_fifty_runs_fails_closed()
    {
        var many = Enumerable.Range(0, 51).Select(i => new CellSource(i.ToString("x64", System.Globalization.CultureInfo.InvariantCulture), "intraday", "E")).ToArray();
        Newest(many);

        var result = await Handler().HandleAsync(new RefreshCellStatsCommand("system:cells-refresh"), Ct);

        result.Error.Code.ShouldBe(CellsRefreshErrors.TooManySourcesCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task With_every_bar_after_the_end_changed_the_snapshot_key_is_unchanged_and_with_a_bar_before_it_the_key_changes()
    {
        // no look-ahead (AC 7): the request never reaches past the end, the run key is a function of the bars up to the request's end, and the snapshot key is a function of the run keys
        Newest(new CellSource(R1, "intraday", "E"));
        Source(R1, Sha1, "ORB", new DateOnly(2026, 3, 31), registered: true);
        var bars = new SortedDictionary<DateOnly, int> { [new DateOnly(2026, 3, 31)] = 100, [End] = 101, [End.AddDays(3)] = 102, [Session] = 103 };
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(call =>
        {
            var to = call.Arg<RunSimulationQuery>().Request.To;
            var text = string.Join(',', bars.Where(b => b.Key <= to).Select(b => $"{b.Key:yyyy-MM-dd}={b.Value}"));
            var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            return Result.Success(FakeRun(key, Sha1, to));
        });
        _runs.FindAsync(Arg.Is<string>(k => k != R1), Ct).Returns(call => Header(call.Arg<string>(), Sha1, "ORB", End));
        _runs.StreamTradesAsync(Arg.Any<string>(), false, Ct).Returns(_ => OneTrade());
        _cells.StoreAsync(Arg.Any<CellSnapshotToStore>(), Ct).Returns(true);
        var realCompute = new ComputeCellStatsHandler(_runs, _cells, Substitute.For<XDrishti.Application.Abstractions.Market.Context.IContextFigures>(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)), NullLogger<ComputeCellStatsHandler>.Instance);
        RefreshCellStatsHandler Real() => new(_runs, _cells, _holdOut, new HoldOutOptions(), _calendar, _strategies, _registrations, _planner, _simulation, _store, _writer, _ledger, realCompute, new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)), NullLogger<RefreshCellStatsHandler>.Instance);

        var before = (await Real().HandleAsync(new RefreshCellStatsCommand("t"), Ct)).Value.SnapshotKey;
        bars[End.AddDays(3)] = 999;
        bars[Session] = -5;
        var after = (await Real().HandleAsync(new RefreshCellStatsCommand("t"), Ct)).Value.SnapshotKey;
        bars[End] = 1;
        var control = (await Real().HandleAsync(new RefreshCellStatsCommand("t"), Ct)).Value.SnapshotKey;

        after.ShouldBe(before);
        control.ShouldNotBe(before);
    }

    private static async IAsyncEnumerable<RunTrade> OneTrade()
    {
        await Task.Yield();
        yield return new RunTrade(FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, End.AddDays(-30), rFilled: 1, rExact: 1, strategy: "ORB"), []);
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

    /// <summary>A ledger whose first append throws, then inserts by run id once (as the database does with ON CONFLICT DO NOTHING).</summary>
    private sealed class FlakyLedger : ITestLedger
    {
        private bool _fail = true;

        public Dictionary<Guid, LedgerRow> Rows { get; } = [];

        public ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken)
        {
            if (_fail)
            {
                _fail = false;
                throw new InvalidOperationException("the database went away");
            }

            return ValueTask.FromResult(Rows.TryAdd(row.RunId, row));
        }

        public ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<TrialCounts> CountTrialsAsync(string strategyId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> HasFullRunAsync(string strategyId, string strategyHash, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> HasHashAsync(string strategyId, string strategyHash, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
