using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P7-14: the auto-journal use case over fake ports. It reads fills and the plan record, links approved tickets, tags the rest, grades only finished round trips beside an existing plan grade, and never changes a ticket.</summary>
public sealed class JournalPlansHandlerTests
{
    private static readonly DateOnly F = new(2026, 3, 3);
    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly string Hash = new('a', 64);
    private static int _n;

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();
    private readonly IJournalStore _journal = Substitute.For<IJournalStore>();
    private readonly IGradeStore _grades = Substitute.For<IGradeStore>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
    private IReadOnlyList<NewJournalLink> _saved = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public JournalPlansHandlerTests()
    {
        _plans.ListAsync(Arg.Any<DateOnly?>(), null, Arg.Any<int>(), Ct).Returns([new PlanRow(1, Plan(), PlanState.Published, 1, 0, 0)]);
        _plans.HistoryAsync(1, Ct).Returns([new PlanEventRow(1, 11, "proposed", "approved", "owner:me", "ok", 1, new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero))]);
        _journal.SaveAsync(Arg.Do<IReadOnlyList<NewJournalLink>>(l => _saved = l), Arg.Any<DateTimeOffset>(), Ct).Returns(new JournalSaved(1, 1));
        _journal.OpenEntriesAsync(Arg.Any<int>(), Ct).Returns([]);
        _journal.PositionsBeforeAsync(Arg.Any<IReadOnlyList<(Guid, string, DateOnly)>>(), Ct).Returns(new Dictionary<(Guid, string, DateOnly), long>());
        _journal.FillsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns([]);
        _grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<long, StoredGrade>());
    }

    private static NewPlan Plan() => new("k", F, new DateTimeOffset(2026, 3, 3, 3, 35, 0, TimeSpan.Zero), 1, new string('b', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []);

    private static PlanItemRow Item(long id, TicketState state, TicketGroup group = TicketGroup.Primary, string symbol = "NSE:RELIANCE", bool allocate = true) =>
        new(id, 1, new NewPlanItem((int)id, group, symbol, "BUY", "ORB", Hash, "t", "s", "g", "v", group == TicketGroup.Primary ? [] : ["why"], new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", 30, "r", "b", null, null, [], null), allocate ? [new NewAllocation(Account, "paper", 10)] : []), state);

    private static JournalFill Fill(string symbol = "RELIANCE", Side side = Side.Buy, int hour = 10, long qty = 10, decimal price = 100m) =>
        new(new Guid(Interlocked.Increment(ref _n), 0, 0, new byte[8]), Account, symbol, side, F, new TimeOnly(hour, 0), qty, price, 0m);

    private static PlanEventRow Approval(long item) => new(item, item, "proposed", "approved", "owner:me", "ok", 1, new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero));

    private JournalPlansHandler Handler() => new(_plans, _journal, _grades, _time, NullLogger<JournalPlansHandler>.Instance);

    [Fact]
    public async Task An_approved_ticket_takes_its_fill_and_every_other_fill_is_tagged_discretionary()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketState.Armed), Item(12, TicketState.Proposed, symbol: "NSE:TCS")]);
        var ticketFill = Fill();
        var other = Fill("TCS"); // TCS's ticket was never approved
        _journal.FillsAsync(F, Arg.Any<DateOnly>(), Ct).Returns([ticketFill, other]);

        var result = await Handler().HandleAsync(new JournalPlansCommand(F), Ct);

        result.Value.Plans.ShouldBe(1);
        result.Value.Tickets.ShouldBe(1);
        result.Value.Entries.ShouldBe(1);
        result.Value.Discretionary.ShouldBe(1);
        _saved.Single(l => l.Link.Role == JournalRole.Entry).Link.ItemId.ShouldBe(11);
        _saved.Single(l => l.Link.Role == JournalRole.Entry).PlanId.ShouldBe(1);
        _saved.Single(l => l.Link.Role == JournalRole.Discretionary).Link.Fill.TransactionId.ShouldBe(other.TransactionId);
    }

    [Fact]
    public async Task A_rejected_candidate_and_an_unapproved_reserve_are_never_matched()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketState.Proposed, TicketGroup.Rejected), Item(12, TicketState.Lapsed, TicketGroup.Reserve, "NSE:TCS")]);
        _journal.FillsAsync(F, Arg.Any<DateOnly>(), Ct).Returns([Fill(), Fill("TCS")]);

        var result = await Handler().HandleAsync(new JournalPlansCommand(F), Ct);

        result.Value.Tickets.ShouldBe(0);
        _saved.ShouldAllBe(l => l.Link.Role == JournalRole.Discretionary);
    }

    [Fact]
    public async Task Ambiguous_fills_are_reported_and_not_stored()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketState.Approved), Item(12, TicketState.Approved)]);
        _plans.HistoryAsync(1, Ct).Returns([Approval(11), Approval(12)]);
        _journal.FillsAsync(F, Arg.Any<DateOnly>(), Ct).Returns([Fill()]);

        var result = await Handler().HandleAsync(new JournalPlansCommand(F), Ct);

        result.Value.Unmatched.Single().Reason.ShouldContain("not guessed");
        _saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fills_are_read_from_the_session_to_the_exit_window_and_never_past_today()
    {
        _plans.ItemsAsync(1, Ct).Returns([]);

        await Handler().HandleAsync(new JournalPlansCommand(F), Ct);
        var earlier = new JournalPlansHandler(_plans, _journal, _grades, new FakeTimeProvider(new DateTimeOffset(2026, 3, 6, 12, 0, 0, TimeSpan.Zero)), NullLogger<JournalPlansHandler>.Instance);
        await earlier.HandleAsync(new JournalPlansCommand(F), Ct);

        var later = new JournalPlansHandler(_plans, _journal, _grades, new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero)), NullLogger<JournalPlansHandler>.Instance);
        await later.HandleAsync(new JournalPlansCommand(F), Ct);

        await _journal.Received(1).FillsAsync(F, new DateOnly(2026, 3, 10), Ct);
        await _journal.Received(1).FillsAsync(F, new DateOnly(2026, 3, 6), Ct);
        await _journal.Received(1).FillsAsync(F, F.AddDays(JournalMatcher.MaxExitDays), Ct);
    }

    [Fact]
    public async Task A_session_that_has_not_begun_is_refused_and_nothing_is_read()
    {
        var result = await Handler().HandleAsync(new JournalPlansCommand(new DateOnly(2026, 3, 11)), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PlanErrors.InvalidCode);
        await _journal.DidNotReceiveWithAnyArgs().FillsAsync(default, default, Ct);
    }

    [Fact]
    public async Task A_finished_round_trip_is_graded_beside_the_plan_grade_and_an_open_or_ungraded_one_waits()
    {
        _plans.ItemsAsync(1, Ct).Returns([]);
        var item = Item(11, TicketState.Closed);
        _plans.FindItemAsync(11, Ct).Returns(item);
        _plans.FindItemAsync(12, Ct).Returns(item with { Id = 12 });
        _plans.FindItemAsync(13, Ct).Returns(item with { Id = 13 });
        StoredFill Leg(string role, string side, int hour, long qty, decimal price) => new(Guid.NewGuid(), Account, "RELIANCE", side, F, new TimeOnly(hour, 0), qty, price, 0m, role, qty, "x");
        _journal.OpenEntriesAsync(Arg.Any<int>(), Ct).Returns(
        [
            new StoredEntry(1, 1, 11, Account, F, [Leg("entry", "buy", 10, 10, 100.5m), Leg("exit", "sell", 13, 10, 104m)], null), // finished, plan grade stored
            new StoredEntry(2, 1, 12, Account, F, [Leg("entry", "buy", 10, 10, 100m)], null), // open
            new StoredEntry(3, 1, 13, Account, F, [Leg("entry", "buy", 10, 10, 100m), Leg("exit", "sell", 13, 10, 101m)], null), // finished, no plan grade yet
        ]);
        var graded = new TicketGrade(GradeStatus.Graded, 2, 0.5, 2.5, "Target", 100, 104, "gross, no charges", "x", 98);
        _grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<long, StoredGrade>
        {
            [11] = new(1, new NewGrade(1, 11, F, graded, null, DateTimeOffset.UnixEpoch)),
            [12] = new(2, new NewGrade(1, 12, F, graded, null, DateTimeOffset.UnixEpoch)),
        });
        IReadOnlyList<NewFillGrade> written = [];
        _journal.InsertGradesAsync(Arg.Do<IReadOnlyList<NewFillGrade>>(g => written = g), Ct).Returns(1);

        var result = await Handler().HandleAsync(new JournalPlansCommand(F), Ct);

        result.Value.GradesWritten.ShouldBe(1);
        result.Value.GradesPending.ShouldBe(2);
        written.Single().EntryId.ShouldBe(1);
        written.Single().Grade.R.ShouldNotBeNull().ShouldBe(1.75, 1e-9);
    }

    [Fact]
    public async Task The_journal_never_moves_a_ticket_or_approves_anything()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketState.Armed)]);
        _journal.FillsAsync(F, Arg.Any<DateOnly>(), Ct).Returns([Fill()]);

        await Handler().HandleAsync(new JournalPlansCommand(F), Ct);

        await _plans.DidNotReceiveWithAnyArgs().MoveItemAsync(default!, Ct);
        await _plans.DidNotReceiveWithAnyArgs().MovePlanAsync(default!, Ct);
        await _plans.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task Running_twice_over_the_same_fills_asks_for_the_same_links()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketState.Armed)]);
        _journal.FillsAsync(F, Arg.Any<DateOnly>(), Ct).Returns([Fill(), Fill(side: Side.Sell, hour: 13)]);

        await Handler().HandleAsync(new JournalPlansCommand(F), Ct);
        var first = _saved;
        await Handler().HandleAsync(new JournalPlansCommand(F), Ct);

        _saved.ShouldBe(first);
    }

    [Fact]
    public async Task An_entry_on_top_of_an_earlier_position_is_stored_as_uncertain()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketState.Armed)]);
        _journal.FillsAsync(F, Arg.Any<DateOnly>(), Ct).Returns([Fill()]);
        _journal.PositionsBeforeAsync(Arg.Any<IReadOnlyList<(Guid, string, DateOnly)>>(), Ct).Returns(new Dictionary<(Guid, string, DateOnly), long> { [(Account, "RELIANCE", F)] = 25 });

        await Handler().HandleAsync(new JournalPlansCommand(F), Ct);

        _saved.Single(l => l.Link.Role == JournalRole.Entry).Uncertain.ShouldNotBeNull().ShouldContain("pre-existing position");
    }

    [Fact]
    public async Task An_uncertain_entry_is_graded_without_an_r()
    {
        _plans.ItemsAsync(1, Ct).Returns([]);
        var item = Item(11, TicketState.Closed);
        _plans.FindItemAsync(11, Ct).Returns(item);
        StoredFill Leg(string role, string side, int hour, decimal price) => new(Guid.NewGuid(), Account, "RELIANCE", side, F, new TimeOnly(hour, 0), 10, price, 0m, role, 10, "x");
        _journal.OpenEntriesAsync(Arg.Any<int>(), Ct).Returns([new StoredEntry(1, 1, 11, Account, F, [Leg("entry", "buy", 10, 100m), Leg("exit", "sell", 13, 104m)], null, "uncertain: pre-existing position (test)")]);
        _grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<long, StoredGrade> { [11] = new(1, new NewGrade(1, 11, F, new TicketGrade(GradeStatus.Graded, 2, 0.5, 2.5, "Target", 100, 104, "gross, no charges", "x", 98), null, DateTimeOffset.UnixEpoch)) });
        IReadOnlyList<NewFillGrade> written = [];
        _journal.InsertGradesAsync(Arg.Do<IReadOnlyList<NewFillGrade>>(g => written = g), Ct).Returns(1);

        var result = await Handler().HandleAsync(new JournalPlansCommand(F), Ct);

        written.Single().Grade.Status.ShouldBe(FillGrade.Uncertain);
        written.Single().Grade.R.ShouldBeNull();
        written.Single().Grade.GrossPnl.ShouldBe(40m);
        result.Value.GradesUncertain.ShouldBe(1);
    }
}
