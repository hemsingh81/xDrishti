#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>P8-11 behind the ports: the stored trade on its bars, adjusted and aggregated on the server, the entry check, the refusals, the honest features and the costing that is shown only for a confirmed sheet.</summary>
public sealed class GetTradeReplayHandlerTests
{
    private static readonly string Run = new('a', 64);
    private static readonly DateOnly Monday = FabricatedRun.Monday;
    private static readonly Guid Id = FabricatedRun.IdA;
    private const string Rates = "rates-hash";

    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly ITradeFeatureStore _features = Substitute.For<ITradeFeatureStore>();
    private readonly IBarStore _bars = Substitute.For<IBarStore>();
    private readonly IBarQueries _queries = Substitute.For<IBarQueries>();
    private readonly IAdjustmentFactors _adjustments = Substitute.For<IAdjustmentFactors>();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();
    private readonly IChargeSheetConfirmationSource _confirmations = Substitute.For<IChargeSheetConfirmationSource>();
    private IReadOnlyList<Bar> _stored = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetTradeReplayHandlerTests()
    {
        _calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<TradingCalendar>(new TradingCalendar()));
        _adjustments.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(new ValueTask<AdjustmentFactors>(AdjustmentFactors.None));
        _queries.FindInstrumentAsync(Id, Arg.Any<CancellationToken>()).Returns(new ValueTask<BarsInstrument?>(new BarsInstrument(Id, "AAA", "Aaa", 5m)));
        _confirmations.FindAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<ChargeSheetConfirmation?>((ChargeSheetConfirmation?)null));
        _features.ReadOneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<TradeFeatureRow?>((TradeFeatureRow?)null));
        _bars.ReadAsync(Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<IReadOnlyList<Bar>>(_stored));
        Store(FabricatedRun.Trade(Id, "NSE:AAA", Side.Buy, Monday));
    }

    private GetTradeReplayHandler Handler() =>
        new(_runs, _features, _bars, _queries, _adjustments, _calendar, _confirmations, new FakeTimeProvider(DateTimeOffset.UnixEpoch), NullLogger<GetTradeReplayHandler>.Instance);

    private void Store(SimTrade trade) =>
        _runs.ReadTradeAsync(Run, false, 0, Arg.Any<CancellationToken>()).Returns(new ValueTask<RunTrade?>(new RunTrade(trade, [FabricatedRun.Basket])));

    /// <summary>One bar a minute on the date from 09:15 to 10:30 India time, all opening at <paramref name="open"/>.</summary>
    private static List<Bar> Session(DateOnly date, double open, int from = 555, int to = 630) =>
        [.. Enumerable.Range(from, to - from).Select(m => new Bar(Day.Epoch(date, m), open, open + 1, open - 1, open, 100))];

    private async Task<TradeReplayView> OkAsync(string? timeframe = null, int? before = null, int? after = null)
    {
        var result = await Handler().HandleAsync(new GetTradeReplayQuery(Run, 0, false, timeframe, before, after), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private async Task<string> CodeAsync(GetTradeReplayQuery query)
    {
        var result = await Handler().HandleAsync(query, Ct);
        result.IsFailure.ShouldBeTrue();
        return result.Error.Code;
    }

    [Fact]
    public async Task A_trade_with_no_legs_has_its_stored_facts_aggregated_bars_and_an_ok_entry_check()
    {
        _stored = Session(Monday, 100.5);

        var view = await OkAsync("5m", 1, 0);

        view.Legs.ShouldBeEmpty();
        view.Basis.ShouldBe("gross");
        view.Costing.ShouldBeNull();
        view.Trade.StopPrice.ShouldBe(98);
        view.Trade.TargetPrice.ShouldBe(105.5);
        view.Trade.ExitReason.ShouldBe("target");
        view.Chart.Timeframe.ShouldBe("5m");
        view.Chart.Bars.Count.ShouldBe(15);
        view.Chart.Bars[0].T.ShouldBe(Day.Epoch(Monday, 555));
        view.Chart.Bars[0].V.ShouldBe(500);
        view.Chart.Coherence.State.ShouldBe("ok");
        view.Evidence.N.ShouldBe(1);
        view.Evidence.Basis.ShouldBe("gross");
    }

    [Fact]
    public async Task Bars_are_adjusted_by_the_factors_and_the_entry_is_checked_on_the_adjusted_price()
    {
        _stored = Session(Monday, 201);
        _adjustments.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(new ValueTask<AdjustmentFactors>(AdjustmentFactors.From([new AppliedAction(Monday.AddDays(8), CorporateActionKind.Split, 1, 2, 0.5m)])));

        var view = await OkAsync("1m");

        view.Chart.Bars[0].O.ShouldBe(100.5);
        view.Chart.Coherence.State.ShouldBe("ok");
        view.Chart.AdjustedThrough.ShouldBe(Monday.AddDays(8));
    }

    [Fact]
    public async Task Bars_that_no_longer_match_the_stored_entry_are_still_returned_with_both_prices()
    {
        _stored = Session(Monday, 50);

        var view = await OkAsync("5m");

        view.Chart.Coherence.State.ShouldBe("entry_mismatch");
        view.Chart.Coherence.EntryStored.ShouldBe(100.5);
        view.Chart.Coherence.EntryOnBars.ShouldBe(50);
        view.Chart.Bars.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task No_bars_in_the_window_is_no_bars_and_the_trade_is_still_there()
    {
        var view = await OkAsync();

        view.Chart.Coherence.State.ShouldBe("no_bars");
        view.Chart.Coherence.EntryOnBars.ShouldBeNull();
        view.Chart.Bars.ShouldBeEmpty();
        view.Trade.Symbol.ShouldBe("NSE:AAA");
    }

    [Fact]
    public async Task A_trade_with_legs_returns_them_in_the_records_order()
    {
        var entry = Day.Epoch(Monday, 600);
        var legs = new XDrishti.Domain.Common.ValueList<Leg>([
            new Leg(entry, 100.5, 100.55, 1m, LegKind.Entry, string.Empty),
            new Leg(entry + 300, 103, 102.95, 0.5m, LegKind.Exit, "partial_1r"),
            new Leg(entry + 600, 105.5, 105.45, 0.5m, LegKind.Exit, "Target")]);
        Store(FabricatedRun.Trade(Id, "NSE:AAA", Side.Buy, Monday) with { Legs = legs });
        _stored = Session(Monday, 100.5);

        var view = await OkAsync();

        view.Legs.Select(l => l.Kind).ShouldBe(["entry", "exit", "exit"]);
        view.Legs[1].Share.ShouldBe(0.5m);
        view.Legs[1].Reason.ShouldBe("partial_1r");
    }

    [Theory]
    [InlineData("10m")]
    [InlineData("1W")]
    [InlineData("bogus")]
    public async Task A_timeframe_outside_the_five_is_invalid(string timeframe) =>
        (await CodeAsync(new GetTradeReplayQuery(Run, 0, false, timeframe, null, null))).ShouldBe("reports.invalid");

    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    [InlineData(-1, 0)]
    public async Task Sessions_outside_zero_to_three_are_invalid_for_an_intraday_timeframe(int before, int after) =>
        (await CodeAsync(new GetTradeReplayQuery(Run, 0, false, "5m", before, after))).ShouldBe("reports.invalid");

    [Fact]
    public async Task The_day_timeframe_takes_up_to_sixty_sessions_and_no_more()
    {
        (await OkAsync("1D", 60, 60)).Chart.Timeframe.ShouldBe("1D");
        (await CodeAsync(new GetTradeReplayQuery(Run, 0, false, "1D", 61, 0))).ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task A_bad_run_key_or_ordinal_is_invalid_and_an_unknown_trade_is_not_found()
    {
        (await CodeAsync(new GetTradeReplayQuery("xyz", 0, false, null, null, null))).ShouldBe("reports.invalid");
        (await CodeAsync(new GetTradeReplayQuery(Run, -1, false, null, null, null))).ShouldBe("reports.invalid");
        var missing = await Handler().HandleAsync(new GetTradeReplayQuery(Run, 7, false, null, null, null), Ct);
        missing.Error.Code.ShouldBe("reports.not_found");
    }

    [Fact]
    public async Task More_than_five_thousand_bars_are_refused_not_thinned()
    {
        _stored = [.. Enumerable.Range(0, 5_001).Select(i => new Bar(Day.Epoch(Monday, 555) + (i * 60L), 100.5, 101, 100, 100.5, 1))];

        (await CodeAsync(new GetTradeReplayQuery(Run, 0, false, "1m", 3, 3))).ShouldBe("reports.window_too_large");
    }

    [Fact]
    public async Task Stored_features_are_listed_by_group_and_an_unknown_value_is_unavailable_never_zero()
    {
        var values = Enumerable.Repeat(1.5, TradeFeatureCatalog.StoredCount).ToArray();
        values[TradeFeatureCatalog.IndexOf("vix")] = double.NaN;
        values[TradeFeatureCatalog.IndexOf("rsi14")] = double.NaN;
        var trade = FabricatedRun.Trade(Id, "NSE:AAA", Side.Buy, Monday);
        _features.ReadOneAsync(Run, trade.Key, TradeFeatureCatalog.Version, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TradeFeatureRow?>(new TradeFeatureRow(Run, trade.Key, "ORB", "15m", "BUY", Monday, trade.DecisionEpochSeconds, values)));

        var view = await OkAsync();

        view.Features.State.ShouldBe("value");
        view.Features.Items.Count.ShouldBe(36);
        view.Features.Items.Select(i => i.Source).Distinct().ShouldBe(["bars", "context", "plan"], ignoreOrder: true);
        var vix = view.Features.Items.Single(i => i.Name == "vix");
        vix.State.ShouldBe("unavailable");
        vix.Value.ShouldBeNull();
        vix.Reason.ShouldBe("market context not built (P3-01d/e)");
        view.Features.Items.Single(i => i.Name == "rsi14").Value.ShouldBeNull();
        view.Features.Items.Single(i => i.Name == "adx14").Value.ShouldBe(1.5);
    }

    [Fact]
    public async Task No_stored_row_says_so_and_a_row_of_another_decision_is_not_shown()
    {
        var none = await OkAsync();
        none.Features.State.ShouldBe("unavailable");
        none.Features.Items.ShouldBeEmpty();
        none.Features.Reason!.ShouldContain("xd learn features");

        var trade = FabricatedRun.Trade(Id, "NSE:AAA", Side.Buy, Monday);
        _features.ReadOneAsync(Run, trade.Key, TradeFeatureCatalog.Version, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TradeFeatureRow?>(new TradeFeatureRow(Run, trade.Key, "ORB", "15m", "BUY", Monday, trade.DecisionEpochSeconds + 60, new double[36])));
        var stale = await OkAsync();
        stale.Features.State.ShouldBe("unavailable");
        stale.Features.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_costed_trade_shows_net_figures_only_for_the_confirmed_sheet()
    {
        var costing = new TradeCosting(CostSegment.IntradayEquity, "sheet-1", 100, 250m, 10000m, 10000m, LegCharges.Zero, LegCharges.Zero, 20.5m, 0.08, 1.87);
        Store(FabricatedRun.Trade(Id, "NSE:AAA", Side.Buy, Monday, costing: costing));
        _runs.FindAsync(Run, Arg.Any<CancellationToken>()).Returns(new ValueTask<StoredRunHeader?>(Header()));

        var unconfirmed = await OkAsync();
        unconfirmed.Basis.ShouldBe("gross");
        unconfirmed.Costing.ShouldNotBeNull();
        unconfirmed.Costing.SheetId.ShouldBe("sheet-1");
        unconfirmed.Costing.Confirmed.ShouldBeFalse();
        unconfirmed.Costing.RNet.ShouldBeNull();
        unconfirmed.Costing.TotalCharges.ShouldBeNull();

        _confirmations.FindAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<ChargeSheetConfirmation?>(new ChargeSheetConfirmation("sheet-1", Rates, new DateOnly(2026, 10, 1))));
        var confirmed = await OkAsync();
        confirmed.Basis.ShouldBe("net");
        confirmed.Costing!.Confirmed.ShouldBeTrue();
        confirmed.Costing.RNet.ShouldBe(1.87);
        confirmed.Costing.TotalCharges.ShouldBe(20.5m);
        confirmed.Evidence.NetBasisConfirmation.ShouldNotBeNull();

        _confirmations.FindAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<ChargeSheetConfirmation?>(new ChargeSheetConfirmation("sheet-1", "other-rates", new DateOnly(2026, 10, 1))));
        (await OkAsync()).Costing!.Confirmed.ShouldBeFalse();
    }

    private static StoredRunHeader Header() => new(
        1, Run, "sim-6", 6, 5, "ORB", "d", Monday, Monday, "list", "x", 1, 1, 1, 1, 1, 0, false, "exact", "sheet-1", Rates, 1, "r", "t", DateTimeOffset.UnixEpoch, "test");
}
