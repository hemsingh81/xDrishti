using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Paper;
using XDrishti.Application.Planning;
using XDrishti.Application.Research;
using XDrishti.Application.Shadow;
using XDrishti.Application.Tests.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;
using static XDrishti.Application.Tests.Shadow.ShadowFixture;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>
/// CF-08 part B over fake ports and the real replayer: a candidate is graded through the one replay path (its R is the P7-13 grade's R and the P10 paper R of the same ticket), a pending replay is not stored and is tried again, a stored grade is never rewritten, an unconfirmed
/// result is stored as the replay gave it, and with the switch OFF nothing is read.
/// </summary>
public sealed class GradeShadowHandlerTests
{
    private static readonly DateOnly F = PaperFixture.Session;

    private static readonly Guid Instrument = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private readonly IShadowSettings _switch = Substitute.For<IShadowSettings>();

    private readonly MemoryGrades _store = new();

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();

    private readonly IGradeSettings _settings = Substitute.For<IGradeSettings>();

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly IBasketQueries _baskets = Substitute.For<IBasketQueries>();

    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)); // the session of 2026-11-03 has closed

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GradeShadowHandlerTests()
    {
        _switch.IsEnabledAsync(Ct).Returns(true);
        _strategies.GetByHashAsync(Hash, Ct).Returns(new StoredVersion("ORB", 1, 1, Hash, "text", "{}", null, null, DateTimeOffset.UnixEpoch));
        var basket = Guid.NewGuid();
        _baskets.ListAsync(false, Ct).Returns([new BasketView(basket, "B", "#000", "p", null, 0, 1, DateTimeOffset.UnixEpoch, null, "manual", false, null, null)]);
        var member = new BasketMemberView(Guid.NewGuid(), RunFixture.Summary(Instrument, "NSE:AAA", null, false), 0, DateTimeOffset.UnixEpoch, null, null, "owner", null, null);
        _baskets.MembersAsync(basket, null, null, Ct).Returns(new BasketMembers(basket, "B", null, null, 1, [member]));
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _settings.Capital.Returns(100_000m);
        _plans.ItemsAsync(1, Ct).Returns([Row(11, 1, Item(1, TicketGroup.Primary, "NSE:AAA"))]);
        _store.Add(new UngradedShadowEntry(501, F, 1, 11));
    }

    private GradeShadowHandler Handler() =>
        new(_switch, _store, _plans, _settings, _sheets, _calendar, new TicketReplayer(_strategies, _baskets, _simulation), _time, NullLogger<GradeShadowHandler>.Instance);

    private void Replay(IReadOnlyList<SimTrade> trades, bool costed = false) =>
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Success(FabricatedRun.Assemble(
            FabricatedRun.List((Instrument, "NSE:AAA")), [FabricatedRun.Ran(Instrument, "NSE:AAA", F.AddDays(-30), F)], trades, null, from: F, to: F, costs: costed ? new CostRecord(PaperFixture.SheetId, new string('a', 64), "{}", null, 100_000m, SizingProfile.Standard, 1m) : null)));

    private async Task<ShadowGradeOutcome> RunAsync()
    {
        var result = await Handler().HandleAsync(new GradeShadowCommand(), Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact]
    public async Task A_candidate_is_graded_gross_with_the_replays_r_entry_exit_and_engine()
    {
        Replay([PaperFixture.Trade(Instrument)]);

        var outcome = await RunAsync();

        outcome.Graded.ShouldBe(1);
        var grade = _store.Stored.Single();
        (grade.EntryId, grade.Status, grade.RBasis, grade.SheetId).ShouldBe((501, "graded", "gross", null));
        grade.RSim.ShouldBe(PaperFixture.RFilled);
        grade.RGross.ShouldBe(PaperFixture.RFilled); // gross basis: the two figures are one
        grade.EntryAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(PaperFixture.EntryEpoch));
        grade.ExitReason.ShouldBe("Target");
        grade.EngineVersion.ShouldNotBeNullOrWhiteSpace();
        grade.EngineVersion.ShouldNotBe("not_replayed");
    }

    [Fact]
    public async Task The_shadow_grade_equals_the_P7_13_grade_and_the_P10_paper_r_of_the_same_ticket_because_all_three_use_the_one_replayer()
    {
        _settings.CostSheetId.Returns(PaperFixture.SheetId);
        _sheets.FindAsync(PaperFixture.SheetId, Ct).Returns(Result.Success(PaperFixture.FlatSheet));
        var trade = PaperFixture.Trade(Instrument, netR: 1.80);
        Replay([trade], costed: true);
        var grades = Substitute.For<IGradeStore>();
        grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<long, StoredGrade>());
        IReadOnlyList<NewGrade>? graded = null;
        grades.InsertAsync(Arg.Do<IReadOnlyList<NewGrade>>(g => graded = g), Ct).Returns(1);
        var p713 = new GradePlansHandler(_plans, grades, _settings, _strategies, _baskets, _sheets, _calendar, _simulation, _time, NullLogger<GradePlansHandler>.Instance);
        _plans.ListAsync(F, null, Arg.Any<int>(), Ct).Returns([new PlanRow(1, new NewPlan("k", F, DateTimeOffset.UnixEpoch, 1, new string('b', 64), "b", true, "t", DateTimeOffset.UnixEpoch, []), PlanState.Published, 1, 0, 0)]);

        await p713.HandleAsync(new GradePlansCommand(F), Ct);
        await RunAsync();

        var shadow = _store.Stored.Single();
        var paper = PaperPnl.Exit(trade, 60, PaperFixture.FlatSheet).Value;
        shadow.RSim.ShouldBe(graded!.Single().Grade.R!.Value); // the P7-13 grade, to the last digit
        shadow.RSim.ShouldBe(paper.RSim); // the P10 paper R
        shadow.RGross.ShouldBe(trade.RFilled); // the gross R of the same trade, beside the net one
        shadow.RGross.ShouldBe(paper.RGross);
        shadow.RGross.ShouldNotBe(shadow.RSim);
        (shadow.RBasis, shadow.SheetId).ShouldBe((paper.RBasis, PaperFixture.SheetId));
        shadow.ExitReason.ShouldBe(graded!.Single().Grade.ExitReason);
    }

    [Fact]
    public async Task A_replay_that_took_no_trade_is_stored_as_not_triggered_with_no_r()
    {
        Replay([]);

        var outcome = await RunAsync();

        outcome.NotTriggered.ShouldBe(1);
        var grade = _store.Stored.Single();
        (grade.Status, grade.RSim, grade.RBasis, grade.EntryAt).ShouldBe(("not_triggered", null, null, null));
    }

    [Fact]
    public async Task A_cell_that_cannot_be_replayed_is_stored_as_not_gradable()
    {
        _plans.ItemsAsync(1, Ct).Returns([Row(11, 1, Item(1, TicketGroup.Primary, "NSE:AAA", cell: "ORB|15m|BUY|intraday|NOT_AN_EXIT|*"))]);

        var outcome = await RunAsync();

        outcome.NotGradable.ShouldBe(1);
        _store.Stored.Single().Status.ShouldBe("not_gradable");
        await _simulation.DidNotReceive().HandleAsync(Arg.Any<RunSimulationQuery>(), Ct);
    }

    [Fact]
    public async Task A_pending_replay_is_not_stored_and_is_tried_again_by_the_next_run_and_a_graded_one_is_never_rewritten()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Failure<SimulationRun>(Error.Conflict("research.sheet_not_in_force", "later")));

        var first = await RunAsync();
        Replay([PaperFixture.Trade(Instrument)]);
        var second = await RunAsync();
        Replay([PaperFixture.Trade(Instrument) with { RFilled = 9 }]); // a different answer later
        var third = await RunAsync();

        first.Pending.ShouldBe(1);
        _store.InsertCalls.ShouldBe(1);
        second.Graded.ShouldBe(1);
        third.Seen.ShouldBe(0); // the entry has its grade: it is not asked again
        _store.Stored.Single().RSim.ShouldBe(PaperFixture.RFilled);
    }

    [Fact]
    public async Task One_candidate_that_fails_is_counted_and_the_others_are_still_graded()
    {
        _store.Add(new UngradedShadowEntry(502, F, 1, 12));
        _plans.ItemsAsync(1, Ct).Returns(
            [Row(11, 1, Item(1, TicketGroup.Primary, "NSE:AAA")), Row(12, 1, Item(2, TicketGroup.Primary, "NSE:AAA", strategy: "ORB") with { Symbol = "NSE:BBB" })]);
        var calls = 0;
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(_ =>
        {
            if (++calls == 1)
            {
                throw new InvalidOperationException("boom with NSE:AAA 123.45");
            }

            return Result.Success(Run([PaperFixture.Trade(Instrument)]));
        });

        var outcome = await RunAsync();

        outcome.Failed.ShouldBe(1);
        outcome.Seen.ShouldBe(2);
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_read_replayed_or_written()
    {
        _switch.IsEnabledAsync(Ct).Returns(false);

        var outcome = await RunAsync();

        outcome.Enabled.ShouldBeFalse();
        outcome.Summary.ShouldContain("Switched off");
        _store.UngradedCalls.ShouldBe(0);
        _store.InsertCalls.ShouldBe(0);
        await _simulation.DidNotReceive().HandleAsync(Arg.Any<RunSimulationQuery>(), Ct);
    }

    [Fact]
    public async Task A_configured_sheet_that_is_not_stored_fails_the_run_and_writes_nothing()
    {
        _settings.CostSheetId.Returns("nse-test");
        _sheets.FindAsync("nse-test", Ct).Returns(Result.Failure<CostSheet>(Error.NotFound("x", "none")));

        var result = await Handler().HandleAsync(new GradeShadowCommand(), Ct);

        result.Error.Code.ShouldBe(PaperErrors.SheetMissingCode);
        _store.InsertCalls.ShouldBe(0);
    }

    [Fact]
    public async Task The_shadow_code_asks_the_replayer_and_never_builds_a_simulation_request_itself()
    {
        Replay([PaperFixture.Trade(Instrument)]);

        await RunAsync();

        await _simulation.Received(1).HandleAsync(Arg.Any<RunSimulationQuery>(), Ct); // an intraday ticket on its own session is one run
    }

    private static SimulationRun Run(IReadOnlyList<SimTrade> trades) =>
        FabricatedRun.Assemble(FabricatedRun.List((Instrument, "NSE:AAA")), [FabricatedRun.Ran(Instrument, "NSE:AAA", F.AddDays(-30), F)], trades, null, from: F, to: F, costs: null);

    private sealed class MemoryGrades : IShadowGradeStore
    {
        private readonly List<UngradedShadowEntry> _entries = [];

        public List<NewShadowGrade> Stored { get; } = [];

        public int InsertCalls { get; private set; }

        public int UngradedCalls { get; private set; }

        public void Add(UngradedShadowEntry entry) => _entries.Add(entry);

        public ValueTask<IReadOnlyList<UngradedShadowEntry>> UngradedAsync(DateOnly through, int take, CancellationToken cancellationToken)
        {
            UngradedCalls++;
            return ValueTask.FromResult<IReadOnlyList<UngradedShadowEntry>>([.. _entries.Where(e => e.SessionDate <= through && Stored.All(g => g.EntryId != e.EntryId)).Take(take)]);
        }

        public ValueTask<int> InsertAsync(IReadOnlyList<NewShadowGrade> grades, CancellationToken cancellationToken)
        {
            InsertCalls++;
            var fresh = grades.Where(g => Stored.All(s => s.EntryId != g.EntryId)).ToList();
            Stored.AddRange(fresh);
            return ValueTask.FromResult(fresh.Count);
        }
    }
}
