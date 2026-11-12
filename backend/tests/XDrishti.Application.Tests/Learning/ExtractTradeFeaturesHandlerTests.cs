using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.History;
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

    private readonly XDrishti.Application.Tests.Market.History.FakeSeams _seams = new();

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
        return new ExtractTradeFeaturesHandler(_runs, _bars, _seams, _factors, _calendar, _extractor, _store, new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<ExtractTradeFeaturesHandler>.Instance);
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
    public async Task Bars_before_the_instruments_seam_never_reach_the_extractor_CF_04_part_B()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day);
        Run(trade);
        var seam = new DateTimeOffset(2024, 4, 2, 9, 15, 0, TimeSpan.FromMinutes(330));
        _seams.Cuts[FabricatedRun.IdA] = seam;
        _runs.ReadRecordTextAsync(Key, Ct).Returns("{\"instruments\":[]}");
        var before = seam.AddDays(-1).ToUnixTimeSeconds();
        _bars.ReadAsync(FabricatedRun.IdA, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct).Returns(new[] { new Bar(before, 1, 1, 1, 1, 1), new Bar(seam.ToUnixTimeSeconds(), 1, 1, 1, 1, 1), new Bar(seam.ToUnixTimeSeconds() + 60, 1, 1, 1, 1, 1) });
        IReadOnlyList<Bar>? seen = null;
        _extractor.Extract(Arg.Do<IReadOnlyList<Bar>>(b => seen = b), Arg.Any<AdjustmentFactors>(), Arg.Any<TradingCalendar>(), Arg.Any<IReadOnlyList<TradeFeatureDecision>>())
            .Returns(new Dictionary<string, double[]> { [trade.Key] = Values() });

        await Handler().HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        seen.ShouldNotBeNull().Select(b => b.EpochSeconds).ShouldBe([seam.ToUnixTimeSeconds(), seam.ToUnixTimeSeconds() + 60]);
    }

    private static readonly DateTimeOffset XSeam = new(2024, 4, 2, 9, 15, 0, TimeSpan.FromMinutes(330));

    private static readonly DateOnly XThrough = new(2026, 4, 30);

    private static readonly AppliedAction XBonus = new(new DateOnly(2024, 8, 1), CorporateActionKind.Bonus, 1, 1, 0.5m);

    private ExtractTradeFeaturesHandler WorldOf(SimTrade trade, bool admittedNow, bool recordSaysAdmitted, out Func<(IReadOnlyList<Bar>? Bars, AdjustmentFactors? Factors)> seen)
    {
        Run(trade);
        var handler = Handler();   // sets the defaults of the fakes; the setup below follows it
        var basis = SeamBasis.Of(XSeam, [XBonus], XThrough);
        _seams.Cuts[FabricatedRun.IdA] = XSeam;
        if (admittedNow)
        {
            _seams.Facts[FabricatedRun.IdA] = new SeamAdmissionFacts(true, new SeamRecord(FabricatedRun.IdA, XSeam, ProbeStatus.Passed, null, 1, 100, null, false, 0, basis, basis), XSeam, XSeam, false);
        }

        _factors.GetAsync(FabricatedRun.IdA, Ct).Returns(AdjustmentFactors.From([XBonus]));
        _factors.CsvAdjustedThroughAsync(FabricatedRun.IdA, Ct).Returns(XThrough);
        var record = recordSaysAdmitted
            ? "{\"instruments\":[{\"instrument_id\":\"" + FabricatedRun.IdA.ToString("D") + "\",\"pre_seam\":{\"seam_date\":\"2024-04-02\",\"basis_sha256\":\"" + basis + "\"}}]}"
            : "{\"instruments\":[{\"instrument_id\":\"" + FabricatedRun.IdA.ToString("D") + "\"}]}";
        _runs.ReadRecordTextAsync(Key, Ct).Returns(record);
        var before = XSeam.AddDays(-1).ToUnixTimeSeconds();
        _bars.ReadAsync(FabricatedRun.IdA, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct).Returns(new[] { new Bar(before, 1, 1, 1, 1, 1), new Bar(XSeam.ToUnixTimeSeconds(), 1, 1, 1, 1, 1) });
        IReadOnlyList<Bar>? bars = null;
        AdjustmentFactors? factors = null;
        _extractor.Extract(Arg.Do<IReadOnlyList<Bar>>(b => bars = b), Arg.Do<AdjustmentFactors>(f => factors = f), Arg.Any<TradingCalendar>(), Arg.Any<IReadOnlyList<TradeFeatureDecision>>())
            .Returns(new Dictionary<string, double[]> { [trade.Key] = Values() });
        seen = () => (bars, factors);
        return handler;
    }

    [Fact]
    public async Task A_run_that_admitted_the_instrument_is_extracted_on_its_pre_seam_bars_and_the_source_aware_factors_CF_04d()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day);
        var handler = WorldOf(trade, admittedNow: true, recordSaysAdmitted: true, out var seen);

        var result = await handler.HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        result.Value.Stale.ShouldBe(0);
        var (bars, factors) = seen();
        bars.ShouldNotBeNull().Count.ShouldBe(2);
        factors.ShouldNotBeNull().FactorAt(new DateOnly(2024, 4, 1)).ShouldBe(0.5);
        factors.FactorAt(new DateOnly(2024, 5, 2)).ShouldBe(1.0, "a CSV bar inside the span is not adjusted again");
    }

    [Fact]
    public async Task An_admitted_run_whose_admission_was_lost_skips_and_counts_the_trades_as_stale_instead_of_double_adjusting_CF_04d()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day);
        var handler = WorldOf(trade, admittedNow: false, recordSaysAdmitted: true, out var seen);

        var result = await handler.HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        result.Value.Stale.ShouldBe(1);
        result.Value.Stored.ShouldBe(0);
        seen().Bars.ShouldBeNull("the extractor was never called");
    }

    [Fact]
    public async Task A_run_that_did_not_admit_is_extracted_cut_with_the_plain_factors_even_when_the_switch_is_on_now_CF_04d()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day);
        var handler = WorldOf(trade, admittedNow: true, recordSaysAdmitted: false, out var seen);

        var result = await handler.HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        result.Value.Stale.ShouldBe(0);
        var (bars, factors) = seen();
        bars.ShouldNotBeNull().Select(b => b.EpochSeconds).ShouldBe([XSeam.ToUnixTimeSeconds()], "the pre-seam bar the engine never saw is not in the lookback");
        factors.ShouldNotBeNull().FactorAt(new DateOnly(2024, 5, 2)).ShouldBe(0.5, "the plain factors, as the run had them");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"no_instruments\":1}")]
    public async Task An_unreadable_run_record_makes_every_instrument_with_a_seam_row_stale_and_one_without_is_read_plain_CF_04d(string? text)
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day);
        var handler = WorldOf(trade, admittedNow: true, recordSaysAdmitted: false, out var seen);
        _runs.ReadRecordTextAsync(Key, Ct).Returns(text);

        var result = await handler.HandleAsync(new ExtractTradeFeaturesCommand(Key), Ct);

        result.Value.Stale.ShouldBe(1);
        seen().Bars.ShouldBeNull();
        RunPreSeam.Parse(text).ShouldBeNull();
        RunPreSeam.Parse("{\"instruments\":[]}").ShouldNotBeNull().ShouldBeEmpty();
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
