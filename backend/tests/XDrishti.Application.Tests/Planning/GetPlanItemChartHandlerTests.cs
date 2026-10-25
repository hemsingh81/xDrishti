using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Planning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P8-14: the ticket chart resolves the symbol to exactly one active share, reads daily bars only up to the planning session (a weekend or holiday skipped), adjusts as of that session, copies the ticket's own levels and pads nothing.</summary>
public sealed class GetPlanItemChartHandlerTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly DateOnly Friday = new(2026, 10, 23);
    private static readonly Guid Instrument = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();
    private readonly IInstrumentQueries _instruments = Substitute.For<IInstrumentQueries>();
    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();
    private readonly IAdjustmentFactors _factors = Substitute.For<IAdjustmentFactors>();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetPlanItemChartHandlerTests()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _factors.GetAsync(Instrument, Ct).Returns(AdjustmentFactors.None);
        _instruments.FindForResolveAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Ct)
            .Returns([new ResolveCandidate(Instrument, "NSE:TCS", "TCS", "share", null, false, true)]);
    }

    private GetPlanItemChartHandler Handler() => new(_plans, _instruments, _bars, _factors, _calendar, NullLogger<GetPlanItemChartHandler>.Instance);

    private void Store(TicketLevels? levels = null)
    {
        var item = new NewPlanItem(
            1, TicketGroup.Primary, "NSE:TCS", "BUY", "ORB", new string('a', 64), "trigger text", "stop text", "target text", "validity text", [],
            new PlanEvidence("cell", 10, "range", "basis", 0.5, 0.1, [], null), [], levels);
        var plan = new NewPlan("k", Monday, new DateTimeOffset(2026, 10, 26, 3, 35, 0, TimeSpan.Zero), 1, new string('c', 64), "basis", true, "t", DateTimeOffset.UnixEpoch, [item]);
        _plans.FindAsync(7, Ct).Returns(new PlanRow(7, plan, PlanState.Published, 1, 0, 0));
        _plans.FindItemAsync(70, Ct).Returns(new PlanItemRow(70, 7, item, TicketState.Proposed));
    }

    private static BarRow Bar(DateOnly day, double close) =>
        new(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset), close, close + 1, close - 1, close, 1000, 375, true, null);

    [Fact]
    public async Task The_session_is_the_trading_day_before_the_plan_date_and_no_bar_after_it_is_asked_for()
    {
        Store();
        _bars.ReadDailyBarsAsync(Instrument, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns([Bar(Friday.AddDays(-1), 99), Bar(Friday, 100)]);

        var result = await Handler().HandleAsync(new GetPlanItemChartQuery(7, 70, 60), Ct);

        var view = result.Value;
        view.Session.ShouldBe(Friday);
        view.Resolution.ShouldBe("exact");
        view.Bars.Select(b => b.C).ShouldBe([99.0, 100.0]);
        view.BarCount.ShouldBe(2);
        view.Levels.Source.ShouldBe("none");
        view.Rules.Trigger.ShouldBe("trigger text");
        await _bars.Received(1).ReadDailyBarsAsync(Instrument, Arg.Any<DateOnly>(), Friday.AddDays(1), Ct);
    }

    [Fact]
    public async Task A_holiday_is_skipped_and_the_levels_session_wins_when_the_ticket_has_levels()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar([TradingDay.Holiday(Friday, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]));
        Store();
        _bars.ReadDailyBarsAsync(Instrument, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns([]);
        (await Handler().HandleAsync(new GetPlanItemChartQuery(7, 70, 60), Ct)).Value.Session.ShouldBe(Friday.AddDays(-1));

        var levels = TicketLevels.Create("BUY", 100m, Friday.AddDays(-2), 95m, StopAnchor.Level, 110m, 2m).Value;
        Store(levels);
        var view = (await Handler().HandleAsync(new GetPlanItemChartQuery(7, 70, 60), Ct)).Value;

        view.Session.ShouldBe(Friday.AddDays(-2));
        view.Levels.Source.ShouldBe("ticket");
        view.Levels.Stop.ShouldBe(95m);
        view.Levels.StopAnchor.ShouldBe("level");
        view.Levels.Target.ShouldBe(110m);
        view.Levels.Basis.ShouldBe(TicketLevelsView.BasisText);
    }

    [Fact]
    public async Task Bars_are_adjusted_only_by_actions_up_to_the_session_and_a_short_history_is_not_padded()
    {
        Store();
        var beforeSplit = Friday.AddDays(-3);
        var splitAt = Friday.AddDays(-1);
        AppliedAction Split(DateOnly ex) => new(ex, CorporateActionKind.Split, 2, 1, 0.5m);
        _factors.GetAsync(Instrument, Ct).Returns(AdjustmentFactors.From([Split(splitAt), Split(Monday.AddDays(5))]));
        _bars.ReadDailyBarsAsync(Instrument, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns([Bar(beforeSplit, 200), Bar(Friday, 100)]);

        var view = (await Handler().HandleAsync(new GetPlanItemChartQuery(7, 70, 60), Ct)).Value;

        view.Bars.Select(b => b.C).ShouldBe([100.0, 100.0]); // the later split (after the session) changes nothing; the earlier one halves the old bar
        view.BarCount.ShouldBe(2);
    }

    [Fact]
    public async Task No_match_or_two_matches_give_no_bars_and_say_so()
    {
        Store();
        _instruments.FindForResolveAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Ct).Returns([]);
        var none = (await Handler().HandleAsync(new GetPlanItemChartQuery(7, 70, 60), Ct)).Value;

        _instruments.FindForResolveAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Ct).Returns([
            new ResolveCandidate(Instrument, "NSE:TCS", "TCS", "share", null, false, true),
            new ResolveCandidate(Guid.NewGuid(), "nse:tcs", "TCS 2", "share", null, false, false),
            new ResolveCandidate(Guid.NewGuid(), "NSE:TCS", "retired", "share", null, true, false)]);
        var two = (await Handler().HandleAsync(new GetPlanItemChartQuery(7, 70, 60), Ct)).Value;

        none.Resolution.ShouldBe("not_found");
        two.Resolution.ShouldBe("ambiguous");
        none.Bars.ShouldBeEmpty();
        two.Bars.ShouldBeEmpty();
        await _bars.DidNotReceiveWithAnyArgs().ReadDailyBarsAsync(default, default, default, Ct);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(251)]
    public async Task Bars_outside_ten_to_250_are_refused(int bars)
    {
        var result = await Handler().HandleAsync(new GetPlanItemChartQuery(7, 70, bars), Ct);

        result.Error.Code.ShouldBe(PlanErrors.InvalidCode);
    }

    [Fact]
    public async Task An_unknown_plan_or_an_item_of_another_plan_is_not_found()
    {
        Store();
        (await Handler().HandleAsync(new GetPlanItemChartQuery(8, 70, 60), Ct)).Error.Code.ShouldBe(PlanErrors.NotFoundCode);
        var seven = await _plans.FindAsync(7, Ct);
        _plans.FindAsync(8, Ct).Returns(new PlanRow(8, seven!.Plan, PlanState.Published, 1, 0, 0));

        (await Handler().HandleAsync(new GetPlanItemChartQuery(8, 70, 60), Ct)).Error.Code.ShouldBe(PlanErrors.ItemNotFoundCode);
    }
}
