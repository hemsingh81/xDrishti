using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Learning.SimTraining;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Domain.Research.SimAll;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning.SimTraining;

/// <summary>
/// AU-13 (ADR 0243 point 4, ADR 0249), with fakes: training reads a completed job through the training reader only, keeps the hold-out out of every row, answers NOT_EVALUABLE with the missing quantity, registers a challenger and nothing else, never an Eligible
/// verdict, and writes once for the same inputs.
/// </summary>
public sealed class TrainFromSimulationHandlerTests
{
    private static readonly string RunKey = new('a', 64);
    private static readonly DateOnly HoldOut = new(2025, 7, 1);
    private static readonly DateOnly WindowTo = new(2025, 6, 27);
    private static readonly DateOnly First = new(2023, 1, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ISimAllStore _jobs = Substitute.For<ISimAllStore>();
    private readonly ITrainingRunReader _reader = Substitute.For<ITrainingRunReader>();
    private readonly ISimTrainingStore _store = Substitute.For<ISimTrainingStore>();
    private readonly ITradeFeatureStore _features = Substitute.For<ITradeFeatureStore>();
    private readonly ITrainingSetStore _sets = Substitute.For<ITrainingSetStore>();
    private readonly IModelTrainer _trainer = Substitute.For<IModelTrainer>();
    private readonly IModelFiles _files = Substitute.For<IModelFiles>();
    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();
    private readonly SimTrainingSettings _settings = new() { Enabled = true, MinRows = 100, MaxRows = 150_000 };
    private SimAllJob _job = Job(SimAllStates.Completed);
    private SimTrainingRecord? _stored;
    private IReadOnlyList<TrainingSimCell>? _cells;
    private DateOnly? _cellsAsOf;
    private IReadOnlyList<ValidationRow>? _trainedOn;
    private ModelToRegister? _registered;

    private static SimAllJob Job(string state) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), state, null, First, WindowTo, HoldOut, 2, "owner:me@example.com", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private readonly ITradeFeatureExtractor _extractor = Substitute.For<ITradeFeatureExtractor>();
    private readonly List<TradeFeatureDecision> _extracted = [];

    private TrainFromSimulationHandler Handler(IScorerFactory? candidate = null)
    {
        _jobs.GetAsync(_job.Id, Ct).Returns(_job);
        _jobs.ListFinishedAsync(_job.Id, Ct).Returns(new SimAllFinished([RunKey], 2));
        _trainer.Candidate.Returns(candidate ?? new Half());
        _trainer.Parameters.Returns("fake");
        _trainer.Train(Arg.Do<IReadOnlyList<ValidationRow>>(r => _trainedOn = r)).Returns(_ =>
            Result.Success(new TrainedModel([1, 2, 3], new string('e', 64), "FastTree", null, TradeFeatureCatalog.Names.Count, 30, 10, [0.0, 1.0], [0.4, 0.6], [.. Enumerable.Repeat(0.1, TradeFeatureCatalog.Names.Count)])));
        _files.WriteAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Ct).Returns(Result.Success());
        _registry.RegisterAsync(Arg.Do<ModelToRegister>(m => _registered = m), Ct).Returns(new RegisteredModel(7, true));
        _store.FindAsync(Arg.Any<string>(), Ct).Returns(_ => _stored);
        _store.StoreAsync(Arg.Do<SimTrainingRecord>(r => _stored = r), Arg.Do<DateOnly>(d => _cellsAsOf = d), Arg.Do<IReadOnlyList<TrainingSimCell>>(c => _cells = c), Ct).Returns(true);
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        var bars = Substitute.For<IBarStore>();
        bars.ReadAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new List<XDrishti.Domain.Market.Bars.Bar> { new(0, 1, 1, 1, 1, 1) }); // one bar: the extractor is asked, and records which decisions it was asked for; the stored features of _features are the ones read
        _extractor.Extract(Arg.Any<IReadOnlyList<XDrishti.Domain.Market.Bars.Bar>>(), Arg.Any<XDrishti.Domain.Market.Actions.AdjustmentFactors>(), Arg.Any<TradingCalendar>(), Arg.Do<IReadOnlyList<TradeFeatureDecision>>(d => _extracted.AddRange(d))).Returns(new Dictionary<string, double[]>());
        return new TrainFromSimulationHandler(_jobs, _reader, _store, bars, new XDrishti.Application.Tests.Market.History.FakeSeams(), Substitute.For<IAdjustmentFactors>(), calendar, _extractor, _features, _sets, _trainer, _files, _registry, _settings,
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)), NullLogger<TrainFromSimulationHandler>.Instance);
    }

    private SimTrade[] History(DateOnly from, DateOnly to)
    {
        var trades = new List<SimTrade>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var i = trades.Count;
            trades.Add(FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, d, i % 3 == 0 ? ExitReason.Target : ExitReason.Stop, rExact: i % 3 == 0 ? 2 : -1, rFilled: i % 3 == 0 ? 2 : -1, fills: FillBasis.Exact));
        }

        _reader.FindAsync(RunKey, Ct).Returns(new TrainingRunHeader(RunKey, "ORB", new string('b', 64), from, to, 1, 1, trades.Count, 0, DateTimeOffset.UnixEpoch));
        _reader.StreamTradesAsync(RunKey, Arg.Any<CancellationToken>()).Returns(_ => Stream(trades));
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), TradeFeatureCatalog.Version, Ct)
            .Returns(trades.Select(t => new TradeFeatureRow(RunKey, t.Key, t.StrategyId, "15m", "BUY", t.TradeDate, t.DecisionEpochSeconds, Enumerable.Repeat(1.0, TradeFeatureCatalog.StoredCount).ToArray())).ToList());
        return [.. trades];
    }

    private static async IAsyncEnumerable<RunTrade> Stream(IEnumerable<SimTrade> trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static readonly TrainFromSimulationCommand Command = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public async Task Off_by_default_nothing_is_read_and_the_answer_is_disabled()
    {
        _settings.Enabled = false;
        History(First, WindowTo);

        var result = await Handler().HandleAsync(Command, Ct);

        result.Error.Code.ShouldBe(SimTrainingErrors.DisabledCode);
        _ = _reader.DidNotReceiveWithAnyArgs().StreamTradesAsync(default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().StoreAsync(default!, default, default!, Ct);
    }

    [Theory]
    [InlineData(SimAllStates.Running)]
    [InlineData(SimAllStates.Paused)]
    [InlineData(SimAllStates.Cancelled)]
    public async Task Only_a_completed_job_is_trained_on(string state)
    {
        _job = Job(state);

        var result = await Handler().HandleAsync(Command, Ct);

        result.Error.Code.ShouldBe(SimTrainingErrors.JobNotFinishedCode);
        await _registry.DidNotReceiveWithAnyArgs().RegisterAsync(default!, Ct);
    }

    [Fact]
    public async Task Too_few_rows_is_NOT_EVALUABLE_naming_the_missing_quantity_and_no_model_is_trained()
    {
        _settings.MinRows = 5000;
        History(First, First.AddDays(200));

        var result = await Handler().HandleAsync(Command, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe("not_evaluable");
        result.Value.NotEvaluable.ShouldNotBeNull().ShouldStartWith("rows: ");
        result.Value.NotEvaluable.ShouldContain("5000 needed");
        result.Value.ModelId.ShouldBeNull();
        result.Value.Summary.ShouldContain("NOT_EVALUABLE");
        _trainer.DidNotReceiveWithAnyArgs().Train(default!);
        await _registry.DidNotReceiveWithAnyArgs().RegisterAsync(default!, Ct);
    }

    [Fact]
    public async Task Enough_rows_but_too_little_history_for_six_folds_names_the_folds()
    {
        History(First, First.AddMonths(11));

        var result = await Handler().HandleAsync(Command, Ct);

        result.Value.State.ShouldBe("not_evaluable");
        result.Value.NotEvaluable.ShouldNotBeNull().ShouldStartWith("walk-forward folds: ");
        result.Value.NotEvaluable.ShouldContain("6 needed");
        await _registry.DidNotReceiveWithAnyArgs().RegisterAsync(default!, Ct);
    }

    [Fact]
    public async Task A_training_registers_a_challenger_only_and_no_row_reaches_the_hold_out_block()
    {
        History(First, new DateOnly(2025, 7, 31));

        var result = await Handler().HandleAsync(Command, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.State.ShouldBe("trained");
        result.Value.Role.ShouldBe("challenger");
        result.Value.HoldOutRead.ShouldBeFalse();
        result.Value.ModelId.ShouldBe(7);
        _trainedOn.ShouldNotBeNull().ShouldNotBeEmpty();
        _trainedOn.ShouldAllBe(r => r.TradeDate < HoldOut);
        _trainedOn.ShouldAllBe(r => r.TradeDate <= WindowTo);
        _registered.ShouldNotBeNull().ToDate.ShouldBeLessThanOrEqualTo(WindowTo);
        _registered.HoldOutStart.ShouldBe(HoldOut);
        _registered.CreatedBy.ShouldBe("system:sim-training");
        _registered.Parameters.ShouldContain("source: training_sim");
        result.Value.Summary.ShouldContain("The hold-out block from 2025-07-01 was not read");
        result.Value.Summary.ShouldContain("challenger only, never promoted by this path");
        await _registry.DidNotReceiveWithAnyArgs().DesignateAsync(default, default, default!, default!, default, default!, default, Ct);
    }

    [Fact]
    public async Task The_verdict_is_never_Eligible_so_this_path_cannot_produce_a_champion()
    {
        History(First, WindowTo);

        var result = await Handler(new Perfect()).HandleAsync(Command, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Verdict.ShouldNotBe(nameof(ValidationOutcome.Eligible));
        _registered.ShouldNotBeNull().Verdict.ShouldNotBe(ValidationOutcome.Eligible);
        _registered.Reasons.ShouldContain("capped below Eligible", Case.Insensitive, "the fixture really earned Eligible, so the cap is what stopped it");
    }

    [Fact]
    public async Task The_summary_has_the_folds_the_rows_and_the_out_of_fold_score_beside_the_baseline()
    {
        History(First, WindowTo);

        var result = await Handler().HandleAsync(Command, Ct);

        var v = result.Value;
        v.FoldsUsed.ShouldBeGreaterThanOrEqualTo(6);
        v.FoldsTotal.ShouldBeGreaterThanOrEqualTo(v.FoldsUsed);
        v.Rows.ShouldBeGreaterThan(100);
        v.Score.ShouldNotBeNull().BaselineName.ShouldNotBeNullOrEmpty();
        v.Summary.ShouldContain("Out-of-fold log loss");
        v.Summary.ShouldContain("for the best baseline");
        v.Summary.ShouldContain("2 chunks not finished");
        v.EmbargoSessions.ShouldBe(5);
        v.EmbargoDays.ShouldBe(7);
    }

    [Fact]
    public async Task The_cell_statistics_go_to_the_training_sim_store_as_of_the_end_of_the_window()
    {
        History(First, WindowTo);

        await Handler().HandleAsync(Command, Ct);

        _cells.ShouldNotBeNull().ShouldNotBeEmpty();
        _cellsAsOf.ShouldBe(WindowTo);
        _cells.ShouldAllBe(c => c.Strategy == "ORB");
    }

    [Fact]
    public async Task The_same_inputs_give_the_same_hash_and_a_second_run_writes_nothing()
    {
        History(First, WindowTo);
        var handler = Handler();

        var first = await handler.HandleAsync(Command, Ct);
        var stored = _stored.ShouldNotBeNull();
        var second = await handler.HandleAsync(Command, Ct);

        first.Value.AlreadyBuilt.ShouldBeFalse();
        second.Value.AlreadyBuilt.ShouldBeTrue();
        second.Value.ModelId.ShouldBe(first.Value.ModelId);
        await _store.Received(1).StoreAsync(Arg.Any<SimTrainingRecord>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<TrainingSimCell>>(), Ct);
        await _registry.Received(1).RegisterAsync(Arg.Any<ModelToRegister>(), Ct);
        stored.InputHash.Length.ShouldBe(64);
    }

    [Fact]
    public async Task A_changed_embargo_is_a_new_training()
    {
        History(First, WindowTo);
        await Handler().HandleAsync(Command, Ct);
        var hash = _stored!.InputHash;
        _stored = null;
        _settings.EmbargoSessions = 10;

        await Handler().HandleAsync(Command, Ct);

        _stored.ShouldNotBeNull().InputHash.ShouldNotBe(hash);
        _stored.EmbargoDays.ShouldBe(14);
    }

    [Fact]
    public async Task Above_the_row_bound_the_rows_are_thinned_by_a_stable_hash_and_the_summary_says_so()
    {
        _settings.MaxRows = 300;
        History(First, WindowTo);

        var first = await Handler().HandleAsync(Command, Ct);
        var firstRows = _trainedOn!.Select(r => r.Key).ToList();
        _stored = null;
        await Handler().HandleAsync(Command, Ct);

        first.Value.SampleEvery.ShouldBeGreaterThan(1);
        first.Value.Rows.ShouldBeLessThan(600);
        first.Value.Summary.ShouldContain($"every {first.Value.SampleEvery}. trade kept");
        _trainedOn!.Select(r => r.Key).ShouldBe(firstRows);
    }

    [Fact]
    public async Task A_run_the_training_reader_does_not_know_is_left_out_and_counted_as_not_finished()
    {
        History(First, WindowTo);
        _reader.FindAsync(RunKey, Ct).Returns((TrainingRunHeader?)null);

        var result = await Handler().HandleAsync(Command, Ct);

        result.Value.State.ShouldBe("not_evaluable");
        result.Value.RunsNotFinished.ShouldBe(3);
        result.Value.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task Only_admitted_and_sampled_trades_are_extracted_and_nothing_of_the_hold_out_is()
    {
        _settings.MaxRows = 300;
        History(First, new DateOnly(2025, 7, 31));

        var result = await Handler().HandleAsync(Command, Ct);

        result.Value.SampleEvery.ShouldBeGreaterThan(1);
        _extracted.ShouldNotBeEmpty();
        _extracted.Count.ShouldBeLessThan(500, "the thinned set, not the 940 trades of the run");
        _extracted.ShouldAllBe(d => SessionDate(d.DecisionEpochSeconds) <= WindowTo);
        _trainedOn!.Count.ShouldBeLessThanOrEqualTo(_extracted.Count);
    }

    [Fact]
    public async Task Memory_stays_bounded_by_the_row_limit_on_a_large_synthetic_job()
    {
        const int Runs = 500;
        const int PerRun = 800; // 400,000 closed trades in all, streamed and never held by the test
        _settings.MaxRows = 40_000;
        var keys = Enumerable.Range(0, Runs).Select(i => i.ToString("x64", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var handler = Handler();
        _jobs.ListFinishedAsync(_job.Id, Ct).Returns(new SimAllFinished(keys, 0));
        var baseline = GC.GetTotalMemory(true);
        long peak = 0;
        var seen = 0;
        foreach (var key in keys.Select((k, i) => (k, i)))
        {
            _reader.FindAsync(key.k, Ct).Returns(new TrainingRunHeader(key.k, "ORB", new string('b', 64), First, WindowTo, 1, 1, PerRun, 0, DateTimeOffset.UnixEpoch));
            _reader.StreamTradesAsync(key.k, Arg.Any<CancellationToken>()).Returns(_ => Generate(key.i, key.k));
            _features.ReadAsync(Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == key.k), TradeFeatureCatalog.Version, Ct).Returns(_ => Task.FromResult<IReadOnlyList<TradeFeatureRow>>([]).Result);
        }

        async IAsyncEnumerable<RunTrade> Generate(int run, string key)
        {
            for (var i = 0; i < PerRun; i++)
            {
                await Task.Yield();
                var t = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:R" + run, Side.Buy, First.AddDays(i), i % 3 == 0 ? ExitReason.Target : ExitReason.Stop, rExact: i % 3 == 0 ? 2 : -1, rFilled: i % 3 == 0 ? 2 : -1, fills: FillBasis.Exact);
                if (++seen % 20_000 == 0)
                {
                    peak = Math.Max(peak, GC.GetTotalMemory(true) - baseline);
                }

                yield return new RunTrade(t, []);
            }
        }

        var result = await handler.HandleAsync(Command, Ct);
        peak = Math.Max(peak, GC.GetTotalMemory(true) - baseline);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.SampleEvery.ShouldBeGreaterThanOrEqualTo(10);
        seen.ShouldBe(Runs * PerRun);
        _extracted.Count.ShouldBeLessThanOrEqualTo(45_000, "only the thinned trades are ever extracted");
        TestContext.Current.SendDiagnosticMessage($"MEMORY peak live heap above baseline: {peak / (1024 * 1024)} MiB for {seen} streamed trades, {_extracted.Count} kept, every {result.Value.SampleEvery}.");
        peak.ShouldBeLessThan(1500L * 1024 * 1024);
    }

    private static DateOnly SessionDate(long epoch) => XDrishti.Domain.Market.Bars.SessionClock.DateOf(epoch);

    private sealed class Half : IScorerFactory
    {
        public string Name => "meta_model";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Fixed();

        private sealed class Fixed : IRowScorer
        {
            public double Score(ValidationRow row) => 0.5;
        }
    }

    /// <summary>Reads the label itself: the best possible score, used to prove that even a perfect-looking model is capped below Eligible.</summary>
    private sealed class Perfect : IScorerFactory
    {
        public string Name => "meta_model";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Cheat();

        private sealed class Cheat : IRowScorer
        {
            public double Score(ValidationRow row) => row.Label == 1 ? 0.99 : 0.01;
        }
    }
}
