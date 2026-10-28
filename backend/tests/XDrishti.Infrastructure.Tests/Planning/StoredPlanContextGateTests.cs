#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Selection.SmartRules;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>P3-01e criteria 7 and 8: the market gate of the plan gets the VIX and the breadth of the latest stored day, in the unit the gate expects, and an honest label; with no figures the gate stays null.</summary>
public sealed class StoredPlanContextGateTests
{
    private static readonly DateOnly Session = new(2026, 10, 9);

    private static readonly DateOnly ForDate = new(2026, 10, 12);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Settings : IOpenBookSettings
    {
        public bool AssumedEmpty => false;

        public bool UsePaperLedger => false;
    }

    private static StoredPlanContextSource Source(ContextRead read, List<DateOnly>? asked = null)
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BrokerAccount>>([]));
        var profiles = Substitute.For<IRiskProfileStore>();
        profiles.ListAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<XDrishti.Domain.Accounts.RiskProfile>>([]));
        var holdings = Substitute.For<IHoldingQueries>();
        holdings.ListAsync(Arg.Any<HoldingFilter>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<HoldingData>>([]));
        var figures = Substitute.For<IContextFigures>();
        figures.AsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            asked?.Add(call.Arg<DateOnly>());
            return ValueTask.FromResult(read);
        });
        return new StoredPlanContextSource(accounts, profiles, holdings, Substitute.For<IHoldingClassifications>(), Substitute.For<IBarQueries>(), new Settings(), Substitute.For<IPaperBookReader>(), figures, Substitute.For<XDrishti.Application.Abstractions.LiveTrading.IRampStore>(), Substitute.For<XDrishti.Application.Abstractions.LiveTrading.ILiveRampSettings>());
    }

    private static ContextRead Of(double? vix, double? breadth, int? universe = 214) =>
        ContextRead.Of(new ContextFigures(Session, "index_only", null, null, null, vix, null, null, null, null, null, breadth, universe));

    private static PlanContextRequest Request => new(Session, ForDate, []);

    [Fact]
    public async Task The_latest_figures_become_the_gate_with_the_breadth_divided_by_100_exactly_once()
    {
        var asked = new List<DateOnly>();

        var ctx = (await Source(Of(19.4, 27.5), asked).ReadAsync(Request, Ct)).Value;

        ctx.Gate.ShouldBe(new MarketGateInput(Session, 19.4, 0.275, null));
        asked.ShouldBe([ForDate], "the figures are those of the latest day strictly before the planned date");
    }

    [Fact]
    public async Task A_breadth_of_a_percent_that_reached_the_gate_would_not_trip_it_so_the_unit_is_pinned_here()
    {
        var ctx = (await Source(Of(12, 27.5), null).ReadAsync(Request, Ct)).Value;

        var gate = MarketGate.Evaluate(ctx.Gate, ForDate, new SmartRulesOptions());
        var asPercent = MarketGate.Evaluate(ctx.Gate! with { Breadth = 27.5 }, ForDate, new SmartRulesOptions());

        (gate.Mode, gate.Triggers.Count).ShouldBe((GateMode.Reduced, 1));
        gate.Triggers[0].ShouldStartWith("breadth 0.275 < 0.3");
        asPercent.Mode.ShouldNotBe(GateMode.Reduced, "27.5 is not below 0.30: a percent in the gate would silently never trip");
    }

    [Fact]
    public async Task With_no_figures_the_gate_stays_null_and_no_market_note_is_written()
    {
        foreach (var reason in new[] { ContextRead.NoContext, ContextRead.BeforeHistory, ContextRead.Stale, ContextRead.Invalid })
        {
            var ctx = (await Source(ContextRead.Not(reason)).ReadAsync(Request, Ct)).Value;

            ctx.Gate.ShouldBeNull(reason);
            ctx.MarketNote.ShouldBeNull(reason);
        }
    }

    [Fact]
    public async Task Figures_with_neither_a_vix_nor_a_breadth_are_no_gate_and_one_known_figure_is_passed_alone()
    {
        (await Source(Of(null, null)).ReadAsync(Request, Ct)).Value.Gate.ShouldBeNull();

        var vixOnly = (await Source(Of(21, null)).ReadAsync(Request, Ct)).Value;
        var breadthOnly = (await Source(Of(null, 40)).ReadAsync(Request, Ct)).Value;

        vixOnly.Gate.ShouldBe(new MarketGateInput(Session, 21, null, null));
        vixOnly.MarketNote.ShouldBeNull("no breadth in the gate, nothing to label");
        breadthOnly.Gate.ShouldBe(new MarketGateInput(Session, null, 0.4, null));
    }

    [Fact]
    public async Task The_note_says_the_breadth_is_the_owners_own_tracked_set_not_the_market()
    {
        var ctx = (await Source(Of(15, 55, 214)).ReadAsync(Request, Ct)).Value;
        var unknownCount = (await Source(Of(15, 55, null)).ReadAsync(Request, Ct)).Value;

        ctx.MarketNote.ShouldBe("Breadth of your 214 tracked shares (members as of today), not of the market.");
        unknownCount.MarketNote.ShouldBe("Breadth of your tracked shares (members as of today), not of the market.");
    }

    [Fact]
    public void The_gate_with_a_real_vix_and_breadth_but_no_events_figure_is_unavailable_unless_something_trips()
    {
        var calm = MarketGate.Evaluate(new MarketGateInput(Session, 12, 0.5, null), ForDate, new SmartRulesOptions());
        var reduced = MarketGate.Evaluate(new MarketGateInput(Session, 19.4, 0.275, null), ForDate, new SmartRulesOptions());

        calm.Mode.ShouldBe(GateMode.Unavailable);
        calm.MissingInputs.ShouldBe(["events"]);
        reduced.Mode.ShouldBe(GateMode.Reduced);
    }
}
