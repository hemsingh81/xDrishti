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

    private static (StoredPlanContextSource Source, IBasketContextStore Store) Source(BasketSettings settings, BasketContextRow? row, SmartRulesOptions? options = null)
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BrokerAccount>>([]));
        var profiles = Substitute.For<IRiskProfileStore>();
        profiles.ListAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<RiskProfile>>([]));
        var holdings = Substitute.For<IHoldingQueries>();
        holdings.ListAsync(Arg.Any<HoldingFilter>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<HoldingData>>([]));
        var store = Substitute.For<IBasketContextStore>();
        store.LatestBeforeAsync(Basket, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(row is not null && row.AsOf < call.ArgAt<DateOnly>(1) ? row : null));
        var queries = Substitute.For<IBasketQueries>();
        queries.GetAsync(Basket, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketView?>(new BasketView(Basket, "Nifty 50", "#fff", "trading", null, 0, 50, DateTimeOffset.UnixEpoch, null, "index", true, null, null)));
        var source = new StoredPlanContextSource(accounts, profiles, holdings, Substitute.For<IHoldingClassifications>(), Substitute.For<IBarQueries>(), new Settings(), Substitute.For<IPaperBookReader>(), Substitute.For<XDrishti.Application.Abstractions.LiveTrading.IRampStore>(), Substitute.For<XDrishti.Application.Abstractions.LiveTrading.ILiveRampSettings>(), Substitute.For<XDrishti.Application.Abstractions.LiveTrading.ILiveReadyFactsSource>(), new FixedSettings(settings), store, queries, options);
        return (source, store);
    }

    private static BasketSettings Gate => BasketSettings.Defaults with { BreadthSource = BasketSettings.SourceOf(Basket) };

    private static PlanContextRequest Request => new(Session, ForDate, []);

    [Fact]
    public async Task With_the_default_source_there_is_no_gate_and_the_basket_store_is_not_read()
    {
        var (source, store) = Source(BasketSettings.Defaults, Row(Session, 0.9));

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate.ShouldBeNull();
        ctx.MarketNote.ShouldBeNull();
        await store.DidNotReceive().LatestBeforeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_row_for_the_prior_session_is_the_breadth_of_the_gate_and_the_note_names_the_basket()
    {
        var (source, _) = Source(Gate, Row(Session, 0.62));

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate.ShouldBe(new MarketGateInput(Session, null, 0.62, null));
        ctx.MarketNote.ShouldBe("breadth of Nifty 50: 0.62");
    }

    [Fact]
    public async Task An_insufficient_row_is_unavailable()
    {
        var (source, _) = Source(Gate, Row(Session, null, "insufficient"));

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate!.Breadth.ShouldBeNull();
        ctx.MarketNote!.ShouldContain("too few members voted");
    }

    [Fact]
    public async Task Without_a_row_there_is_no_gate_and_the_note_says_why()
    {
        var (source, _) = Source(Gate, null);

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate.ShouldBeNull();
        ctx.MarketNote!.ShouldContain("unavailable");
    }

    [Fact]
    public async Task A_row_older_than_the_configured_maximum_age_is_unavailable_even_when_the_default_would_accept_it()
    {
        var (source, _) = Source(Gate, Row(Session, 0.62), new SmartRulesOptions(GateMaxAgeDays: 2)); // the row is 3 days before the planned day

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Gate!.Breadth.ShouldBeNull();
        ctx.MarketNote!.ShouldContain("stale");
    }
}
