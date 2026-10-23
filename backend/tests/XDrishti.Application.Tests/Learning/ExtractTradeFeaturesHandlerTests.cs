using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The extraction use case (spec P6-04): what the extractor is told about a trade, idempotence, the missing-bars case and the refusals before any read.</summary>
public sealed class ExtractTradeFeaturesHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static readonly DateOnly Day = new(2026, 3, 3);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly IBarStore _bars = Substitute.For<IBarStore>();

    private readonly IAdjustmentFactors _factors = Substitute.For<IAdjustmentFactors>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly ITradeFeatureExtractor _extractor = Substitute.For<ITradeFeatureExtractor>();

    private readonly ITradeFeatureStore _store = Substitute.For<ITradeFeatureStore>();

    private IReadOnlyList<TradeFeatureRow>? _written;

    private ExtractTradeFeaturesHandler Handler()
    {
        _factors.GetAsync(Arg.Any<Guid>(), Ct).Returns(AdjustmentFactors.None);
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _store.StoreAsync(Arg.Do<IReadOnlyList<TradeFeatureRow>>(r => _written = r), Arg.Any<DateTimeOffset>(), Ct).Returns(ci => ci.ArgAt<IReadOnlyList<TradeFeatureRow>>(0).Count);
        return new ExtractTradeFeaturesHandler(_runs, _bars, _factors, _calendar, _extractor, _store, new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<ExtractTradeFeaturesHandler>.Instance);
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

    private void Run(params SimTrade[] trades)
    {
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(trades));
    }

    private static double[] Values() => Enumerable.Repeat(0.5, TradeFeatureCatalog.StoredCount).ToArray();

    [Fact]
    public async Task The_extractor_is_told_only_the_plan_of_each_trade_and_its_rows_are_stored()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Sell, Day, ExitReason.Stop, stop: 101, target: 97, rExact: -1, rFilled: -1.05);
        Run(trade);
        _bars.ReadAsync(FabricatedRun.IdA, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct).Returns(new[] { new Bar(1_000, 1, 1, 1, 1, 1) });
        IReadOnlyList<TradeFeatureDecision>? told = null;
        _extractor.Extract(Arg.Any<IReadOnlyList<Bar>>(), Arg.Any<AdjustmentFactors>(), Arg.Any<TradingCalendar>(), Arg.Do<IReadOnlyList<TradeFeatureDecision>>(d => told = d))
            .Returns(new Dictionary<string, double[]> { [trade.Key] = Values() });

        var result = await Handler().HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new TradeFeaturesOutcome(1, 1, 0));
        told.ShouldNotBeNull().ShouldBe([new TradeFeatureDecision(trade.Key, Timeframe.M15, trade.DecisionEpochSeconds, Side.Sell, 101, 97)]);
        var row = _written.ShouldNotBeNull().Single();
        row.RunKey.ShouldBe(Key);
        row.TradeKey.ShouldBe(trade.Key);
        row.Side.ShouldBe("SELL");
        row.Timeframe.ShouldBe("15m");
        row.DecisionEpochSeconds.ShouldBe(trade.DecisionEpochSeconds);
    }

    [Fact]
    public async Task A_trade_whose_instrument_has_no_stored_bars_is_counted_and_gets_no_row()
    {
        Run(FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day));
        _bars.ReadAsync(FabricatedRun.IdA, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct).Returns(Array.Empty<Bar>());

        var result = await Handler().HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        result.Value.ShouldBe(new TradeFeaturesOutcome(1, 0, 1));
        _written.ShouldNotBeNull().ShouldBeEmpty();
        _extractor.DidNotReceiveWithAnyArgs().Extract(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Trades_are_grouped_by_instrument_and_each_instruments_bars_are_read_once()
    {
        var a1 = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day);
        var a2 = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day.AddDays(1));
        var b1 = FabricatedRun.Trade(FabricatedRun.IdB, "NSE:B", Side.Buy, Day);
        Run(a1, b1, a2);
        _bars.ReadAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct).Returns(new[] { new Bar(1_000, 1, 1, 1, 1, 1) });
        _extractor.Extract(Arg.Any<IReadOnlyList<Bar>>(), Arg.Any<AdjustmentFactors>(), Arg.Any<TradingCalendar>(), Arg.Any<IReadOnlyList<TradeFeatureDecision>>())
            .Returns(ci => ci.ArgAt<IReadOnlyList<TradeFeatureDecision>>(3).ToDictionary(d => d.TradeKey, _ => Values()));

        var result = await Handler().HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        result.Value.ShouldBe(new TradeFeaturesOutcome(3, 3, 0));
        await _bars.Received(1).ReadAsync(FabricatedRun.IdA, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct);
        await _bars.Received(1).ReadAsync(FabricatedRun.IdB, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct);
    }

    [Fact]
    public async Task A_bad_run_key_or_actor_is_refused_before_anything_is_read()
    {
        var result = await Handler().HandleAsync(new ExtractTradeFeaturesCommand("not-a-key"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().StoreAsync(default!, default, Ct);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        _runs.FindAsync(Key, Ct).Returns((StoredRunHeader?)null);

        var result = await Handler().HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }
}
