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

/// <summary>The drift replay use case (spec P6-09): stored run to drift events, the refusals, and that nothing is written.</summary>
public sealed class ReplayDriftHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private static readonly string Hash = new('e', 64);

    private static readonly DateOnly Day = new(2026, 1, 5);

    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();

    private readonly ITradeFeatureStore _features = Substitute.For<ITradeFeatureStore>();

    private readonly IModelFiles _files = Substitute.For<IModelFiles>();

    private readonly IModelScorerLoader _loader = Substitute.For<IModelScorerLoader>();

    private ReplayDriftHandler Handler() => new(_runs, _features, _registry, _files, _loader, new FakeTimeProvider(), NullLogger<ReplayDriftHandler>.Instance);

    private static StoredRunHeader Header() =>
        new(1, Key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static ReplayDriftQuery Query(string runKey = "", int months = 1, long? model = null) =>
        new([new CellSource(runKey.Length > 0 ? runKey : Key, "intraday", "FIXED_RR")], new DriftOptions(), months, model, new CellStatsOptions(), null);

    /// <summary>One trade a day for 90 days; 60 reference days of +-1 around 0.3 R, then 30 days of -0.5 R.</summary>
    private static SimTrade[] Trades() =>
        [.. Enumerable.Range(0, 90).Select(i =>
        {
            var r = i < 60 ? (i % 2 == 0 ? 1.3 : -0.7) : -1.0;
            return FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, new DateOnly(2026, 1, 1).AddDays(i), ExitReason.Stop, rExact: r, rFilled: r);
        })];

    [Fact]
    public async Task A_decaying_cell_in_the_last_month_raises_a_cusum_alarm_and_a_proposal()
    {
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(Trades()));

        var result = await Handler().HandleAsync(Query(), Ct);

        result.IsSuccess.ShouldBeTrue();
        var cell = result.Value.Cells.ShouldHaveSingleItem();
        cell.CellKey.ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|*");
        cell.Band.ShouldNotBeNull();
        cell.Events.ShouldContain(e => e.Detector == DriftDetectorKind.Cusum && e.Fired);
        result.Value.Proposals.ShouldHaveSingleItem().Pause.ShouldBeTrue();
        result.Value.Calibration.ShouldBeNull();
    }

    [Fact]
    public async Task A_bad_request_is_refused_before_any_read()
    {
        var bad = await Handler().HandleAsync(Query("nope"), Ct);
        var months = await Handler().HandleAsync(Query(months: 0), Ct);

        bad.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        months.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        _runs.FindAsync(Key, Ct).Returns((StoredRunHeader?)null);

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    [Fact]
    public async Task An_unknown_model_is_not_found_and_nothing_is_designated()
    {
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(Trades()));
        _registry.FindAsync(9, Ct).Returns((StoredModel?)null);

        var result = await Handler().HandleAsync(Query(model: 9), Ct);

        result.Error.Code.ShouldBe(ModelErrors.NotFoundCode);
        _registry.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Distinct().ShouldBe(["FindAsync"]);
    }

    private static StoredModel Model(DateOnly toDate, bool heldOut = true, string version = "tf-1", int? width = null) =>
        new(7, ModelRole.Challenger, new ModelToRegister(
            new string('f', 64), Hash, 3, "FastTree", null, new string('a', 64), version, width ?? TradeFeatureCatalog.Names.Count, 30, 10, 40, Day, toDate, heldOut ? toDate.AddDays(30) : null, ValidationOutcome.Eligible, 6, 2, 4, false, 7.5, 0.7, 0.25,
            string.Empty, "report", "params", [0, 1], [0.4, 0.6], [.. Enumerable.Repeat(0.5, width ?? TradeFeatureCatalog.Names.Count)], 1, 0, DateTimeOffset.UnixEpoch, "t"),
            1);

    /// <summary>120 trades three days apart, alternating target and stop, with every feature known.</summary>
    private SimTrade[] Run()
    {
        var trades = Enumerable.Range(0, 120)
            .Select(i => FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day.AddDays(i * 3), i % 2 == 0 ? ExitReason.Target : ExitReason.Stop, rExact: i % 2 == 0 ? 2 : -1, rFilled: i % 2 == 0 ? 2 : -1, fills: FillBasis.Exact))
            .ToArray();
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(trades));
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), "tf-1", Ct)
            .Returns(trades.Select(t => new TradeFeatureRow(Key, t.Key, t.StrategyId, "15m", "BUY", t.TradeDate, t.DecisionEpochSeconds, Enumerable.Repeat(1.0, TradeFeatureCatalog.StoredCount).ToArray())).ToList());
        return trades;
    }

    private static ReplayDriftQuery ModelQuery() =>
        new([new CellSource(Key, "intraday", "FIXED_RR")], new DriftOptions(CalibrationWindow: 20, CalibrationCheckEvery: 10), 1, 7, new CellStatsOptions(PriorStrength: 2, MinTrades: 2), ["cell_n", "rsi14"]);

    private void GivenModel(StoredModel model, double probability = 0.9)
    {
        var bytes = new byte[] { 1, 2, 3 };
        var scorer = Substitute.For<IExplainableScorer>();
        scorer.Score(Arg.Any<ValidationRow>()).Returns(probability);
        _registry.FindAsync(7, Ct).Returns(model);
        _files.ReadAsync(Hash, Ct).Returns(Result.Success(bytes));
        _loader.Load(bytes, Hash).Returns(Result.Success<IExplainableScorer>(scorer));
    }

    [Fact]
    public async Task A_model_is_scored_on_the_trades_after_its_last_training_day_and_the_outcomes_reach_the_detector()
    {
        var trades = Run();
        var toDate = new DateOnly(2026, 6, 1);
        GivenModel(Model(toDate));

        var result = await Handler().HandleAsync(ModelQuery(), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var calibration = result.Value.Calibration.ShouldNotBeNull();
        calibration.Subject.ShouldBe("model:7");
        calibration.Outcomes.ShouldBe(trades.Count(t => t.TradeDate > toDate));
        calibration.Outcomes.ShouldBeLessThan(trades.Length);
        calibration.Events.ShouldContain(e => e.Detector == DriftDetectorKind.Calibration && e.Fired);
        calibration.Note.ShouldContain("after 2026-06-01");
        calibration.Note.ShouldContain("ignores --monitor-months");
        calibration.Note.ShouldNotContain("no hold-out");
        _loader.Received(1).Load(Arg.Any<byte[]>(), Hash);
        _registry.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Distinct().ShouldBe(["FindAsync"]);
    }

    [Fact]
    public async Task A_model_without_a_hold_out_says_so()
    {
        Run();
        GivenModel(Model(new DateOnly(2026, 6, 1), heldOut: false));

        var result = await Handler().HandleAsync(ModelQuery(), Ct);

        result.Value.Calibration!.Note.ShouldContain("kept no hold-out");
    }

    [Fact]
    public async Task A_missing_or_corrupt_model_file_is_refused()
    {
        Run();
        GivenModel(Model(new DateOnly(2026, 6, 1)));
        _files.ReadAsync(Hash, Ct).Returns(Result.Failure<byte[]>(ModelErrors.FileMissing(Hash)));

        var missing = await Handler().HandleAsync(ModelQuery(), Ct);

        GivenModel(Model(new DateOnly(2026, 6, 1)));
        _loader.Load(Arg.Any<byte[]>(), Hash).Returns(Result.Failure<IExplainableScorer>(ModelErrors.FileCorrupt(Hash)));
        var corrupt = await Handler().HandleAsync(ModelQuery(), Ct);

        missing.Error.Code.ShouldBe(ModelErrors.FileMissingCode);
        corrupt.Error.Code.ShouldBe(ModelErrors.FileCorruptCode);
    }

    [Theory]
    [InlineData("tf-0", 0)]
    [InlineData("tf-1", 3)]
    public async Task A_model_of_another_feature_version_or_width_is_refused(string version, int width)
    {
        Run();
        GivenModel(Model(new DateOnly(2026, 6, 1), version: version, width: width == 0 ? null : width));

        var result = await Handler().HandleAsync(ModelQuery(), Ct);

        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
    }
}
