using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The meta-labelling policy use case (spec P6-13): a champion is required, it must match the data and the trainer, the hold-out is cut and stated, a bad request reads nothing.</summary>
public sealed class EvaluateMetaPolicyHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static readonly DateOnly Day = new(2026, 1, 5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ITradeFeatureStore _features = Substitute.For<ITradeFeatureStore>();

    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();

    private readonly IModelTrainer _trainer = Substitute.For<IModelTrainer>();

    private EvaluateMetaPolicyHandler Handler()
    {
        _trainer.Candidate.Returns(new ByLabel());
        _trainer.Parameters.Returns("fake");
        return new EvaluateMetaPolicyHandler(_runs, _features, _registry, _trainer, new FakeTimeProvider(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<EvaluateMetaPolicyHandler>.Instance);
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

    private void Run(int count)
    {
        var trades = Enumerable.Range(0, count)
            .Select(i => FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day.AddDays(i * 3), i % 2 == 0 ? ExitReason.Target : ExitReason.Stop, rExact: i % 2 == 0 ? 2 : -1, rFilled: i % 2 == 0 ? 2 : -1, fills: FillBasis.Exact))
            .ToArray();
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(trades));
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), "tf-1", Ct).Returns(trades.Select(t =>
            new TradeFeatureRow(Key, t.Key, t.StrategyId, "15m", "BUY", t.TradeDate, t.DecisionEpochSeconds, Enumerable.Repeat(1.0, TradeFeatureCatalog.StoredCount).ToArray())).ToList());
    }

    private static EvaluateMetaPolicyQuery Query(int holdOutMonths = 0) =>
        new([new CellSource(Key, "intraday", "FIXED_RR")], new CellStatsOptions(PriorStrength: 2, MinTrades: 2), ["cell_n", "rsi14"], new ValidationOptions(MinTrainMonths: 1, TestMonths: 1, MinTrainTrades: 5, MinFoldsForVerdict: 1), new MetaPolicyOptions(MinThresholdRows: 5), holdOutMonths);

    private static StoredModel Champion(string trainingSetId, string parameters = "trainer: fake; validation: x") =>
        new(7, ModelRole.Champion, new ModelToRegister(
            new string('f', 64), new string('e', 64), 3, "FastTree", null, trainingSetId, "tf-1", 3, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), null, ValidationOutcome.Eligible, 6, 4, 4, true, 2, 0.6, 0.2,
            string.Empty, "report", parameters, [0, 1], [0.4, 0.6], [0.1, 0.1, 0.1], 1, 0, DateTimeOffset.UnixEpoch, "t"), 1);

    private async Task<string> TrainingSetId()
    {
        var loaded = await TrainingDataLoader.LoadAsync(_runs, _features, Query().Sources, Query().Options, Query().Required, Ct);
        return loaded.Value.Data.Id;
    }

    [Fact]
    public async Task Without_a_champion_the_answer_is_no_policy_and_no_run_is_read()
    {
        _registry.ListAsync(ModelRole.Champion, 1, Ct).Returns([]);

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Value.Outcome.ShouldBe(MetaPolicyOutcome.NoPolicy);
        result.Value.Reasons.Single().ShouldContain("no champion", Case.Insensitive);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task A_champion_on_another_training_set_gives_no_policy()
    {
        Run(60);
        var champion = Champion(new string('9', 64));
        _registry.ListAsync(ModelRole.Champion, 1, Ct).Returns([champion]);
        _registry.FindAsync(7, Ct).Returns(champion);

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Value.Outcome.ShouldBe(MetaPolicyOutcome.NoPolicy);
        result.Value.Reasons.Single().ShouldContain("training set");
    }

    [Fact]
    public async Task A_champion_made_with_other_trainer_settings_gives_no_policy()
    {
        var champion = Champion(new string('9', 64), "trainer: other; validation: x");
        _registry.ListAsync(ModelRole.Champion, 1, Ct).Returns([champion]);
        _registry.FindAsync(7, Ct).Returns(champion);

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Value.Outcome.ShouldBe(MetaPolicyOutcome.NoPolicy);
        result.Value.Reasons.Single().ShouldContain("trainer settings");
    }

    [Fact]
    public async Task A_matching_champion_is_evaluated_after_the_hold_out_is_cut_and_stated()
    {
        Run(60);
        var champion = Champion(await TrainingSetId());
        _registry.ListAsync(ModelRole.Champion, 1, Ct).Returns([champion]);
        _registry.FindAsync(7, Ct).Returns(champion);

        var result = await Handler().HandleAsync(Query(holdOutMonths: 2), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Outcome.ShouldNotBe(MetaPolicyOutcome.NoPolicy);
        result.Value.Limits.ShouldContain(l => l.Contains("held out", StringComparison.Ordinal));
        result.Value.Limits.ShouldContain(l => l.Contains("Champion: model 7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_hold_out_the_report_says_so()
    {
        Run(60);
        var champion = Champion(await TrainingSetId());
        _registry.ListAsync(ModelRole.Champion, 1, Ct).Returns([champion]);
        _registry.FindAsync(7, Ct).Returns(champion);

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Value.Limits.ShouldContain(l => l.Contains("No hold-out was kept", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bad_request_is_refused_before_any_read()
    {
        var result = await Handler().HandleAsync(Query() with { HoldOutMonths = 500, Policy = new MetaPolicyOptions(MinKeptShare: 0) }, Ct);

        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _registry.DidNotReceiveWithAnyArgs().ListAsync(default, default, Ct);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    /// <summary>A model that reads the label: perfect, only for the use case test.</summary>
    private sealed class ByLabel : IScorerFactory
    {
        public string Name => "meta_model";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Read();

        private sealed class Read : IRowScorer
        {
            public double Score(ValidationRow row) => row.Label;
        }
    }
}
