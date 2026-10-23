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

/// <summary>The training-set use case (spec P6-04): labels joined to stored features and to the cell statistics of the day before each decision, the stated rule for unknowns, idempotence and the refusals.</summary>
public sealed class BuildTrainingSetHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static readonly DateOnly Day = new(2026, 3, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ITradeFeatureStore _features = Substitute.For<ITradeFeatureStore>();

    private readonly ITrainingSetStore _store = Substitute.For<ITrainingSetStore>();

    private TrainingSetToStore? _written;

    private BuildTrainingSetHandler Handler(bool newRecord = true)
    {
        _store.StoreAsync(Arg.Do<TrainingSetToStore>(r => _written = r), Ct).Returns(newRecord);
        return new BuildTrainingSetHandler(_runs, _features, _store, new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<BuildTrainingSetHandler>.Instance);
    }

    private static StoredRunHeader Header() =>
        new(1, Key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static SimTrade Trade(int dayOffset, ExitReason exit, double r) =>
        FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day.AddDays(dayOffset), exit, rExact: r, rFilled: r, fills: FillBasis.Exact);

    private static TradeFeatureRow Row(SimTrade t) =>
        new(Key, t.Key, t.StrategyId, "15m", "BUY", t.TradeDate, t.DecisionEpochSeconds, Enumerable.Repeat(1.0, TradeFeatureCatalog.StoredCount).ToArray());

    private SimTrade[] Run(params SimTrade[] trades)
    {
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(trades));
        return trades;
    }

    private static BuildTrainingSetCommand Command(IReadOnlyList<string>? required = null) =>
        new([new CellSource(Key, "intraday", "FIXED_RR")], new CellStatsOptions(PriorStrength: 2, MinTrades: 2), required ?? ["cell_n", "rsi14"], "test");

    [Fact]
    public async Task Each_trade_sees_the_cell_statistics_of_the_trades_that_exited_before_its_decision_day()
    {
        var trades = Run(Trade(0, ExitReason.Target, 2), Trade(1, ExitReason.Stop, -1), Trade(2, ExitReason.Target, 2), Trade(3, ExitReason.Stop, -1));
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), "tf-1", Ct).Returns(trades.Select(Row).ToList());

        var result = await Handler().HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldBe(4);
        result.Value.Positives.ShouldBe(2);
        result.Value.Negatives.ShouldBe(2);
        result.Value.AlreadyStored.ShouldBeFalse();
        var d = _written.ShouldNotBeNull().Data;
        d.Rows.Select(r => r.Values[TradeFeatureCatalog.IndexOf("cell_n")]).ShouldBe([0, 1, 2, 3], "the first trade has no past; each later one sees all the earlier ones and nothing of its own");
        d.Rows.Select(r => r.Values[TradeFeatureCatalog.IndexOf("cell_enough")]).ShouldBe([0, 0, 1, 1]);
        d.FeatureVersion.ShouldBe("tf-1");
        _written.Sources.ShouldBe($"{Key}:intraday:FIXED_RR");
        _written.CreatedBy.ShouldBe("test");
    }

    [Fact]
    public async Task Trades_without_features_or_a_label_are_left_out_and_counted()
    {
        var trades = Run(Trade(0, ExitReason.Target, 2), Trade(1, ExitReason.Stop, -1), Trade(2, ExitReason.EndOfData, 0.3));
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), "tf-1", Ct).Returns(new List<TradeFeatureRow> { Row(trades[0]) });

        var result = await Handler().HandleAsync(Command(), Ct);

        result.Value.Rows.ShouldBe(1);
        result.Value.Unresolved.ShouldBe(1);
        result.Value.DroppedNoFeatures.ShouldBe(1);
    }

    [Fact]
    public async Task A_stored_row_with_another_decision_instant_is_dropped_and_counted()
    {
        var trades = Run(Trade(0, ExitReason.Target, 2), Trade(1, ExitReason.Stop, -1));
        var rows = trades.Select(Row).ToList();
        rows[1] = rows[1] with { DecisionEpochSeconds = rows[1].DecisionEpochSeconds + 60 };
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), "tf-1", Ct).Returns(rows);

        var result = await Handler().HandleAsync(Command(), Ct);

        result.Value.Rows.ShouldBe(1);
        result.Value.DroppedNoFeatures.ShouldBe(1);
    }

    [Fact]
    public async Task A_second_identical_build_writes_nothing_and_says_so()
    {
        var trades = Run(Trade(0, ExitReason.Target, 2));
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), "tf-1", Ct).Returns(trades.Select(Row).ToList());

        var result = await Handler(newRecord: false).HandleAsync(Command(), Ct);

        result.Value.AlreadyStored.ShouldBeTrue();
        result.Value.Id.Length.ShouldBe(64);
    }

    [Fact]
    public async Task A_feature_name_outside_the_catalog_or_a_bad_run_is_refused_before_any_read()
    {
        var handler = Handler();

        var name = await handler.HandleAsync(Command(["nonsense"]), Ct);
        var run = await handler.HandleAsync(new BuildTrainingSetCommand([new CellSource("short", "weekly", "A|B")], new CellStatsOptions(), null, "test"), Ct);

        name.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        run.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_run_and_a_run_with_nothing_to_label_are_refused_without_writing()
    {
        _runs.FindAsync(Key, Ct).Returns((StoredRunHeader?)null);
        var handler = Handler();

        var missing = await handler.HandleAsync(Command(), Ct);
        Run(Trade(0, ExitReason.EndOfData, 0));
        var empty = await handler.HandleAsync(Command(), Ct);

        missing.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
        empty.Error.Code.ShouldBe("training_set.empty");
        await _store.DidNotReceiveWithAnyArgs().StoreAsync(default!, Ct);
    }

    [Fact]
    public async Task Features_that_are_all_unknown_refuse_the_set_and_the_message_counts_the_loss()
    {
        var trades = Run(Trade(0, ExitReason.Target, 2));
        _features.ReadAsync(Arg.Any<IReadOnlyList<string>>(), "tf-1", Ct).Returns(new List<TradeFeatureRow>());

        var result = await Handler().HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe("training_data.empty");
        trades.Length.ShouldBe(1);
        await _store.DidNotReceiveWithAnyArgs().StoreAsync(default!, Ct);
    }
}
