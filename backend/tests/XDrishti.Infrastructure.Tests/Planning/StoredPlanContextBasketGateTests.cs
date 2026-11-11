#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>BK-08 criterion 9: with the gate's breadth source set to a basket the gate reads the basket's stored row of the prior session and names it; no row, a stale row or an insufficient row is unavailable, never the tracked set's figure. With the default the stored figures are used as before.</summary>
public sealed class StoredPlanContextBasketGateTests
{
    private static readonly DateOnly Session = new(2026, 10, 9);

    private static readonly DateOnly ForDate = new(2026, 10, 12);

    private static readonly Guid Basket = new("00000000-0000-0000-0000-0000000000b1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Settings : IOpenBookSettings
    {
        public bool AssumedEmpty => false;

        public bool UsePaperLedger => false;
    }

    private sealed class FixedSettings(BasketSettings settings) : IBasketSettingsStore
    {
        public ValueTask<BasketSettingsState> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new BasketSettingsState(settings, 0));

        public ValueTask<IReadOnlyList<BasketSettingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<BasketSettingChangeRow>>([]);

        public ValueTask<bool> AppendAsync(IReadOnlyList<BasketSettingChange> rows, long expectedVersion, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private static BasketContextRow Row(DateOnly day, double? b20, string state = "value") => new(Basket, day, 50, 48, b20, null, null, null, null, null, null, state, "single_snapshot");

    private static (StoredPlanContextSource Source, IBasketContextStore Store) Source(BasketSettings settings, BasketContextRow? row, ContextRead read, SmartRulesOptions? options = null)
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BrokerAccount>>([]));
        var profiles = Substitute.For<IRiskProfileStore>();
        profiles.ListAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<RiskProfile>>([]));
        var holdings = Substitute.For<IHoldingQueries>();
        holdings.ListAsync(Arg.Any<HoldingFilter>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<HoldingData>>([]));
        var figures = Substitute.For<IContextFigures>();
        figures.AsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(read));
        var store = Substitute.For<IBasketContextStore>();
        store.LatestBeforeAsync(Basket, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(row is not null && row.AsOf < call.ArgAt<DateOnly>(1) ? row : null));
        var queries = Substitute.For<IBasketQueries>();
        queries.GetAsync(Basket, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketView?>(new BasketView(Basket, "Nifty 50", "#fff", "trading", null, 0, 50, DateTimeOffset.UnixEpoch, null, "index", true, null, null)));
        var source = new StoredPlanContextSource(accounts, profiles, holdings, Substitute.For<IHoldingClassifications>(), Substitute.For<IBarQueries>(), new Settings(), Substitute.For<IPaperBookReader>(), figures, Substitute.For<XDrishti.Application.Abstractions.LiveTrading.IRampStore>(), Substitute.For<XDrishti.Application.Abstractions.LiveTrading.ILiveRampSettings>(), Substitute.For<XDrishti.Application.Abstractions.LiveTrading.ILiveReadyFactsSource>(), new FixedSettings(settings), store, queries, options);
        return (source, store);
    }

    private static ContextRead Tracked(double? vix = 19.4, double? breadth = 27.5) =>
        ContextRead.Of(new ContextFigures(Session, "index_only", null, null, null, vix, null, null, null, null, null, breadth, 214));

    private static BasketSettings Gate => BasketSettings.Defaults with { BreadthSource = BasketSettings.SourceOf(Basket) };

    private static PlanContextRequest Request => new(Session, ForDate, []);

    [Fact]
    public async Task With_the_default_source_the_tracked_breadth_is_used_exactly_as_before_and_the_basket_store_is_not_read()
    {
        var (source, store) = Source(BasketSettings.Defaults, Row(Session, 0.9), Tracked());

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate.ShouldBe(new MarketGateInput(Session, 19.4, 0.275, null));
        ctx.MarketNote.ShouldBe("Breadth of your 214 tracked shares (members as of today), not of the market.");
        await store.DidNotReceive().LatestBeforeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_row_for_the_prior_session_is_the_breadth_of_the_gate_the_tracked_figure_is_ignored_and_the_note_names_the_basket()
    {
        var (source, _) = Source(Gate, Row(Session, 0.62), Tracked());

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate.ShouldBe(new MarketGateInput(Session, 19.4, 0.62, null));
        ctx.MarketNote.ShouldBe("breadth of Nifty 50: 0.62");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2026-10-12", 0.6)] // a row of the planned day itself is not known before it
    [InlineData("2026-10-05", 0.6)] // older than the maximum age
    public async Task No_row_a_future_row_or_a_stale_row_is_unavailable_and_never_the_tracked_set(string? date, double? b20)
    {
        var row = date is null ? null : Row(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), b20);
        var (source, _) = Source(Gate, row, Tracked());

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate!.Breadth.ShouldBeNull();
        ctx.Gate.Breadth.ShouldNotBe(0.275);
        ctx.MarketNote!.ShouldContain("unavailable");
        ctx.MarketNote!.ShouldContain("Nifty 50");
        MarketGate.Evaluate(ctx.Gate with { Vix = 12, HighImpactEventToday = false }, ForDate, new SmartRulesOptions()).Mode.ShouldBe(GateMode.Unavailable);
    }

    [Fact]
    public async Task An_insufficient_row_is_unavailable()
    {
        var (source, _) = Source(Gate, Row(Session, null, "insufficient"), Tracked());

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate!.Breadth.ShouldBeNull();
        ctx.MarketNote!.ShouldContain("too few members voted");
    }

    [Fact]
    public async Task Without_stored_market_figures_a_basket_row_still_gives_a_gate_with_no_vix()
    {
        var (source, _) = Source(Gate, Row(Session, 0.62), ContextRead.Not(ContextRead.NoContext));

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate.ShouldBe(new MarketGateInput(Session, null, 0.62, null));
    }

    [Fact]
    public async Task Without_figures_and_without_a_row_there_is_no_gate_and_the_note_says_why()
    {
        var (source, _) = Source(Gate, null, ContextRead.Not(ContextRead.NoContext));

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate.ShouldBeNull();
        ctx.MarketNote!.ShouldContain("unavailable");
    }

    [Fact]
    public async Task A_row_older_than_the_configured_maximum_age_is_unavailable_even_when_the_default_would_accept_it()
    {
        var (source, _) = Source(Gate, Row(Session, 0.62), Tracked(), new SmartRulesOptions(GateMaxAgeDays: 2)); // the row is 3 days before the planned day

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate!.Breadth.ShouldBeNull();
        ctx.MarketNote!.ShouldContain("stale");
    }

    [Fact]
    public async Task The_input_is_stamped_with_the_older_of_the_vix_day_and_the_row_day()
    {
        var older = Session.AddDays(-1);
        var (source, _) = Source(Gate, Row(Session, 0.62), ContextRead.Of(new ContextFigures(older, "index_only", null, null, null, 19.4, null, null, null, null, null, 27.5, 214)));

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate!.AsOf.ShouldBe(older);
        ctx.Gate.Breadth.ShouldBe(0.62);
    }
}
