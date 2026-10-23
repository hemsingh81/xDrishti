using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The train, validate and register use case (spec P6-07): the training-set record and the file are written, the model is registered as a challenger whatever its verdict, the hold-out is in neither the verdict nor the model, the same inputs register once.</summary>
public sealed class TrainModelHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static readonly DateOnly Day = new(2026, 1, 5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ITradeFeatureStore _features = Substitute.For<ITradeFeatureStore>();

    private readonly ITrainingSetStore _sets = Substitute.For<ITrainingSetStore>();

    private readonly IModelTrainer _trainer = Substitute.For<IModelTrainer>();

    private readonly IModelFiles _files = Substitute.For<IModelFiles>();

    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();

    private ModelToRegister? _registered;

    private IReadOnlyList<ValidationRow>? _trainedOn;

    private TrainModelHandler Handler(bool created = true)
    {
        _trainer.Candidate.Returns(new ConstantScorer());
        _trainer.Parameters.Returns("fake");
        _trainer.Train(Arg.Do<IReadOnlyList<ValidationRow>>(r => _trainedOn = r)).Returns(_ =>
            Result.Success(new TrainedModel([1, 2, 3], new string('e', 64), "FastTree", null, TradeFeatureCatalog.Names.Count, 30, 10, [0.0, 1.0], [0.4, 0.6], [.. Enumerable.Repeat(0.1, TradeFeatureCatalog.Names.Count)])));
        _files.WriteAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Ct).Returns(Result.Success());
        _registry.RegisterAsync(Arg.Do<ModelToRegister>(m => _registered = m), Ct).Returns(new RegisteredModel(7, created));
        return new TrainModelHandler(_runs, _features, _sets, _trainer, _files, _registry, new FakeTimeProvider(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<TrainModelHandler>.Instance);
    }

    private static StoredRunHeader Header() =>
        new(1, Key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static TradeFeatureRow Row(SimTrade t) =>
        new(Key, t.Key, t.StrategyId, "15m", "BUY", t.TradeDate, t.DecisionEpochSeconds, Enumerable.Repeat(1.0, TradeFeatureCatalog.StoredCount).ToArray());

    private SimTrade[] Run(int count)
    {
        var trades = Enumerable.Range(0, count)
            .Select(i => FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day.AddDays(i * 3), i % 2 == 0 ? ExitReason.Target : ExitReason.Stop, rExact: i % 2 == 0 ? 2 : -1, rFilled: i % 2 == 0 ? 2 : -1, fills: FillBasis.Exact))
            .ToArray();
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(trades));
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), "tf-1", Ct).Returns(trades.Select(Row).ToList());
        return trades;
    }

    private static TrainModelCommand Command(int holdOutMonths = 0) =>
        new([new CellSource(Key, "intraday", "FIXED_RR")], new CellStatsOptions(PriorStrength: 2, MinTrades: 2), ["cell_n", "rsi14"], new ValidationOptions(MinTrainMonths: 1, TestMonths: 1, MinTrainTrades: 5, MinFoldsForVerdict: 1), holdOutMonths, "test");

    [Fact]
    public async Task A_model_that_cannot_beat_the_baselines_is_still_registered_with_its_verdict_the_training_set_and_the_file()
    {
        Run(60);

        var result = await Handler().HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.ModelId.ShouldBe(7);
        result.Value.AlreadyRegistered.ShouldBeFalse();
        result.Value.Verdict.ShouldBe(nameof(ValidationOutcome.CannotBeatBaselines));
        var m = _registered.ShouldNotBeNull();
        m.Verdict.ShouldBe(ValidationOutcome.CannotBeatBaselines);
        m.Sha256.ShouldBe(new string('e', 64));
        m.TrainingSetId.ShouldBe(result.Value.TrainingSetId);
        m.ReportText.ShouldContain("Replayed history");
        m.ReportText.ShouldContain("No hold-out was kept");
        m.Reasons.ShouldNotBeEmpty();
        m.CreatedBy.ShouldBe("test");
        await _sets.Received(1).StoreAsync(Arg.Is<TrainingSetToStore>(s => s.Data.Id == m.TrainingSetId), Ct);
        await _files.Received(1).WriteAsync(new string('e', 64), Arg.Any<byte[]>(), Ct);
    }

    [Fact]
    public async Task The_hold_out_is_in_neither_the_final_model_nor_the_validation()
    {
        Run(60);

        var result = await Handler().HandleAsync(Command(holdOutMonths: 2), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var start = result.Value.HoldOutStart.ShouldNotBeNull();
        _trainedOn.ShouldNotBeNull().ShouldNotBeEmpty();
        _trainedOn.ShouldAllBe(r => r.TradeDate < start);
        _registered.ShouldNotBeNull().ToDate.ShouldBeLessThan(start);
        _registered.ReportText.ShouldContain("held out");
    }

    [Fact]
    public async Task The_trials_come_from_the_registry_and_the_command_line_can_only_raise_them()
    {
        Run(60);
        var handler = Handler();
        _registry.CountOtherModelsAsync("tf-1", new string('e', 64), Ct).Returns(4);

        await handler.HandleAsync(Command(), Ct);
        var derived = _registered.ShouldNotBeNull();
        await handler.HandleAsync(Command() with { Validation = Command().Validation! with { Trials = 9 } }, Ct);
        var floor = _registered.ShouldNotBeNull();

        derived.Trials.ShouldBe(5);
        derived.Parameters.ShouldContain("trials=5");
        derived.ReportText.ShouldContain("Trials allowed for: 5");
        floor.Trials.ShouldBe(9);
    }

    [Fact]
    public async Task The_purged_development_rows_are_in_the_report_and_the_record()
    {
        Run(60);

        await Handler().HandleAsync(Command(holdOutMonths: 2), Ct);

        var m = _registered.ShouldNotBeNull();
        m.PurgedRows.ShouldBeGreaterThanOrEqualTo(0);
        m.ReportText.ShouldContain($"{m.PurgedRows} development trades were purged");
    }

    [Fact]
    public async Task The_same_inputs_give_the_same_registration_key_and_a_second_registration_says_so()
    {
        Run(60);
        var first = await Handler().HandleAsync(Command(), Ct);
        var key = _registered!.VersionKey;

        var second = await Handler(created: false).HandleAsync(Command(), Ct);

        second.Value.AlreadyRegistered.ShouldBeTrue();
        _registered!.VersionKey.ShouldBe(key);
        first.Value.TrainingSetId.ShouldBe(second.Value.TrainingSetId);
    }

    [Fact]
    public async Task A_bad_command_is_refused_before_any_read()
    {
        var handler = Handler();

        var result = await handler.HandleAsync(Command() with { HoldOutMonths = 500, Validation = new ValidationOptions(Trials: 0) }, Ct);

        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
        await _registry.DidNotReceiveWithAnyArgs().RegisterAsync(default!, Ct);
    }

    [Fact]
    public async Task A_model_that_cannot_be_trained_registers_nothing_and_writes_no_file()
    {
        Run(60);
        var handler = Handler();
        _trainer.Train(Arg.Any<IReadOnlyList<ValidationRow>>()).Returns(Result.Failure<TrainedModel>(Error.Validation("ml.too_few_rows", "too few")));

        var result = await handler.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe("ml.too_few_rows");
        await _registry.DidNotReceiveWithAnyArgs().RegisterAsync(default!, Ct);
        await _files.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_file_that_cannot_be_written_registers_nothing()
    {
        Run(60);
        var handler = Handler();
        _files.WriteAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Ct).Returns(Result.Failure(ModelErrors.FileCorrupt(new string('e', 64))));

        var result = await handler.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(ModelErrors.FileCorruptCode);
        await _registry.DidNotReceiveWithAnyArgs().RegisterAsync(default!, Ct);
    }

    /// <summary>A candidate that always says one half: it cannot beat a baseline.</summary>
    private sealed class ConstantScorer : IScorerFactory
    {
        public string Name => "meta_model";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Half();

        private sealed class Half : IRowScorer
        {
            public double Score(ValidationRow row) => 0.5;
        }
    }
}
