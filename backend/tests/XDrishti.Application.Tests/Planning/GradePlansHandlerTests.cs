using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Planning;
using XDrishti.Application.Research;
using XDrishti.Application.Tests.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P7-13: the grading use case over fake ports. It asks for exactly the planned session, writes each ticket once, leaves pending tickets unwritten, and never guesses a ticket it cannot replay.</summary>
public sealed class GradePlansHandlerTests
{
    private static readonly DateOnly F = new(2026, 3, 3);

    private static readonly DateOnly D = new(2026, 3, 2);

    private static readonly Guid Instrument = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*";

    private static readonly string Hash = new('a', 64);

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();

    private readonly IGradeStore _grades = Substitute.For<IGradeStore>();

    private readonly IGradeSettings _settings = Substitute.For<IGradeSettings>();

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly IBasketQueries _baskets = Substitute.For<IBasketQueries>();

    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GradePlansHandlerTests()
    {
        _strategies.GetByHashAsync(Hash, Ct).Returns(new StoredVersion("ORB", 1, 1, Hash, "text", "{}", null, null, DateTimeOffset.UnixEpoch));
        var basket = Guid.NewGuid();
        _baskets.ListAsync(false, Ct).Returns([new BasketView(basket, "B", "#000", "p", null, 0, 1, DateTimeOffset.UnixEpoch, null, "manual", false, null, null)]);
        var member = new BasketMemberView(Guid.NewGuid(), RunFixture.Summary(Instrument, "NSE:AAA", null, false), 0, DateTimeOffset.UnixEpoch, null, null, "owner", null, null);
        _baskets.MembersAsync(basket, null, null, Ct).Returns(new BasketMembers(basket, "B", null, null, 1, [member]));
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<long, StoredGrade>());
        _plans.ListAsync(Arg.Any<DateOnly?>(), null, Arg.Any<int>(), Ct).Returns([new PlanRow(1, Plan(), PlanState.Published, 1, 0, 0)]);
    }

    private static NewPlan Plan() => new("k", F, new DateTimeOffset(2026, 3, 3, 3, 35, 0, TimeSpan.Zero), 1, new string('b', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []);

    private static PlanItemRow Item(long id, TicketGroup group, string symbol = "NSE:AAA", string cell = Cell) =>
        new(id, 1, new NewPlanItem((int)id, group, symbol, "BUY", "ORB", Hash, "t", "s", "g", "v", group == TicketGroup.Primary ? [] : ["why"], new PlanEvidence(cell, 30, "r", "b", null, null, [], null), []), TicketState.Proposed);

    private GradePlansHandler Handler() => new(_plans, _grades, _settings, _strategies, _baskets, _sheets, _calendar, _simulation, _time, NullLogger<GradePlansHandler>.Instance);

    private void Replay(IReadOnlyList<SimTrade> trades, DateOnly? last = null) =>
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Success(FabricatedRun.Assemble(
            FabricatedRun.List((Instrument, "NSE:AAA")), [FabricatedRun.Ran(Instrument, "NSE:AAA", F.AddDays(-30), last ?? F)], trades, from: F, to: F)));

    [Fact]
    public async Task An_intraday_ticket_is_replayed_over_exactly_its_own_session_with_its_stored_version_and_exit_and_written_once()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketGroup.Primary)]);
        Replay([FabricatedRun.Trade(Instrument, "NSE:AAA", Side.Buy, F, rFilled: 1.5)]);

        var result = await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        result.Value.ShouldBe(new GradeOutcome(1, 1, 0, 0, 0, 0));
        await _simulation.Received(1).HandleAsync(
            Arg.Is<RunSimulationQuery>(q => q.Request.From == F && q.Request.To == F && q.Request.StrategyHash == Hash && q.Request.InstrumentIds!.Single() == Instrument
                && q.Request.Exits!.Candidates.Single().Id == "FIXED_RR{rr=2}" && q.Request.Costs == CostChoice.Gross && q.Request.EvaluationTimeframe == "15m"), Ct);
        await _grades.Received(1).InsertAsync(Arg.Is<IReadOnlyList<NewGrade>>(g => g.Count == 1 && g[0].ItemId == 11 && g[0].Grade.R == 1.5 && g[0].Grade.Status == GradeStatus.Graded), Ct);
    }

    [Fact]
    public async Task All_three_groups_are_graded_and_an_unreplayable_ticket_is_stored_not_gradable_with_its_reason()
    {
        _plans.ItemsAsync(1, Ct).Returns([
            Item(11, TicketGroup.Primary),
            Item(12, TicketGroup.Reserve),
            Item(13, TicketGroup.Rejected, cell: "ORB|15m|BUY|intraday|FIXED_RR|*"),
            Item(14, TicketGroup.Rejected, cell: "ORB|15m|BUY|intraday|PARTIAL_BE|*")]);
        Replay([FabricatedRun.Trade(Instrument, "NSE:AAA", Side.Buy, F, rFilled: 1)]);

        var result = await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        result.Value.Graded.ShouldBe(2);
        result.Value.NotGradable.ShouldBe(2);
        await _grades.Received(1).InsertAsync(
            Arg.Is<IReadOnlyList<NewGrade>>(g => g.Count == 4 && g.Single(x => x.ItemId == 13).Grade.Detail.Contains("exit label", StringComparison.Ordinal) && g.Single(x => x.ItemId == 14).Grade.Detail.Contains("exit label", StringComparison.Ordinal)), Ct);
        await _simulation.Received(2).HandleAsync(Arg.Any<RunSimulationQuery>(), Ct);
    }

    [Fact]
    public async Task A_ticket_that_already_has_a_grade_is_not_replayed_or_written_again()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketGroup.Primary)]);
        _grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<long, StoredGrade> { [11] = new(1, new NewGrade(1, 11, F, TicketGrader.NotGradable("x"), null, DateTimeOffset.UnixEpoch)) });

        var result = await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        result.Value.AlreadyGraded.ShouldBe(1);
        await _simulation.DidNotReceive().HandleAsync(Arg.Any<RunSimulationQuery>(), Ct);
        await _grades.DidNotReceive().InsertAsync(Arg.Any<IReadOnlyList<NewGrade>>(), Ct);
    }

    [Fact]
    public async Task A_ticket_whose_bars_do_not_reach_the_session_is_pending_and_nothing_is_written()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketGroup.Primary)]);
        Replay([], last: F.AddDays(-1));

        var result = await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        result.Value.Pending.ShouldBe(1);
        await _grades.DidNotReceive().InsertAsync(Arg.Any<IReadOnlyList<NewGrade>>(), Ct);
    }

    [Fact]
    public async Task A_session_that_has_not_ended_cannot_be_graded()
    {
        var result = await Handler().HandleAsync(new GradePlansCommand(new DateOnly(2026, 3, 10)), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PlanErrors.InvalidCode);
        await _plans.DidNotReceive().ListAsync(Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Ct);
    }

    [Fact]
    public async Task A_swing_ticket_with_an_intraday_trigger_is_read_from_F_to_the_last_closed_session_with_a_second_run_from_the_next_day_to_scope_the_fates()
    {
        var swing = "ORB|15m|BUY|swing|FIXED_RR{rr=2}|*";
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketGroup.Primary, cell: swing)]);
        Replay([FabricatedRun.Trade(Instrument, "NSE:AAA", Side.Buy, F)]);

        await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        await _simulation.Received(1).HandleAsync(Arg.Is<RunSimulationQuery>(q => q.Request.From == F && q.Request.To == new DateOnly(2026, 3, 10)), Ct);
        await _simulation.Received(1).HandleAsync(Arg.Is<RunSimulationQuery>(q => q.Request.From == F.AddDays(1) && q.Request.To == new DateOnly(2026, 3, 10)), Ct);
    }

    [Fact]
    public async Task A_daily_trigger_ticket_is_read_from_D_with_a_second_run_from_F()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketGroup.Primary, cell: "ORB|1D|BUY|swing|FIXED_RR{rr=2}|*")]);
        Replay([]);

        await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        await _simulation.Received(1).HandleAsync(Arg.Is<RunSimulationQuery>(q => q.Request.From == D && q.Request.To == new DateOnly(2026, 3, 10)), Ct);
        await _simulation.Received(1).HandleAsync(Arg.Is<RunSimulationQuery>(q => q.Request.From == F && q.Request.To == new DateOnly(2026, 3, 10)), Ct);
    }

    [Fact]
    public async Task The_grades_of_all_due_plans_are_read_once()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketGroup.Primary)]);
        Replay([]);

        await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        await _grades.Received(1).ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct);
    }

    [Fact]
    public async Task A_configured_sheet_that_is_not_stored_fails_the_run_with_no_silent_gross_fallback()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketGroup.Primary)]);
        _settings.CostSheetId.Returns("nse-test");
        _sheets.FindAsync("nse-test", Ct).Returns(Result.Failure<CostSheet>(Error.NotFound("x", "none")));

        var result = await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PlanErrors.GradingSheetMissingCode);
        await _simulation.DidNotReceive().HandleAsync(Arg.Any<RunSimulationQuery>(), Ct);
        await _grades.DidNotReceive().InsertAsync(Arg.Any<IReadOnlyList<NewGrade>>(), Ct);
    }

    [Fact]
    public async Task A_refused_replay_and_a_symbol_that_left_every_basket_are_pending_not_final()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketGroup.Primary), Item(12, TicketGroup.Reserve, symbol: "NSE:ZZZ")]);
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Failure<SimulationRun>(Error.Conflict("simulation.cost_sheet_not_effective", "not in force")));

        var result = await Handler().HandleAsync(new GradePlansCommand(F), Ct);

        result.Value.Pending.ShouldBe(2);
        await _grades.DidNotReceive().InsertAsync(Arg.Any<IReadOnlyList<NewGrade>>(), Ct);
    }
}
