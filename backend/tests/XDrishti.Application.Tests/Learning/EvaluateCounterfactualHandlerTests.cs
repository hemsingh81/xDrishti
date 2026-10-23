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

/// <summary>The frozen-model counterfactual use case (spec P6-12): the hold-out is cut and stated, no champion is needed, a bad request reads nothing.</summary>
public sealed class EvaluateCounterfactualHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static readonly DateOnly Day = new(2025, 1, 5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ITradeFeatureStore _features = Substitute.For<ITradeFeatureStore>();

    private readonly IModelTrainer _trainer = Substitute.For<IModelTrainer>();

    private EvaluateCounterfactualHandler Handler()
    {
        _trainer.Candidate.Returns(new ByLabel());
        _trainer.Parameters.Returns("fake");
        return new EvaluateCounterfactualHandler(_runs, _features, _trainer, new FakeTimeProvider(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<EvaluateCounterfactualHandler>.Instance);
    }

    private static StoredRunHeader Header() =>
        new(1, Key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2025, 1, 1), new DateOnly(2026, 8, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

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

    private static EvaluateCounterfactualQuery Query(int holdOutMonths = 0) =>
        new([new CellSource(Key, "intraday", "FIXED_RR")], new CellStatsOptions(PriorStrength: 2, MinTrades: 2), ["cell_n", "rsi14"], new ValidationOptions(MinTrainMonths: 3, TestMonths: 1, MinTrainTrades: 5), new CounterfactualOptions(MinWindowRows: 3), holdOutMonths);

    [Fact]
    public async Task Without_a_champion_it_evaluates_the_recipe_and_states_the_hold_out()
    {
        Run(200);

        var result = await Handler().HandleAsync(Query(holdOutMonths: 2), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Windows.ShouldNotBeEmpty();
        result.Value.Limits.ShouldContain(l => l.Contains("held out", StringComparison.Ordinal));
        result.Value.Limits.ShouldContain(l => l.Contains("Model recipe: meta_model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_held_out_months_are_never_scored()
    {
        Run(200);

        var held = await Handler().HandleAsync(Query(holdOutMonths: 3), Ct);
        var open = await Handler().HandleAsync(Query(), Ct);

        held.Value.To.ShouldBeLessThan(open.Value.To);
    }

    [Fact]
    public async Task Without_a_hold_out_the_report_says_so()
    {
        Run(200);

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Value.Limits.ShouldContain(l => l.Contains("No hold-out was kept", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bad_request_is_refused_before_any_read()
    {
        var result = await Handler().HandleAsync(Query() with { HoldOutMonths = 500, Counterfactual = new CounterfactualOptions(FrozenMonths: 30) }, Ct);

        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
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
