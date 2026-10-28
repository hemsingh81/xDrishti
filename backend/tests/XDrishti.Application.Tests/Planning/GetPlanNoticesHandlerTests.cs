using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P8-15: the handler gathers the stored plans, the newest run and the stored fills through read members only, and hands them to the rules. The window is three India days, the newest version of a session counts, and a fill is matched to a ticket by account, symbol, side and day.</summary>
public sealed class GetPlanNoticesHandlerTests
{
    private static readonly DateOnly Session = new(2026, 10, 13);
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 13, 3, 35, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly string Hash = new('a', 64);

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();
    private readonly IJournalStore _journal = Substitute.For<IJournalStore>();
    private readonly ITicketNoticeSource _tickets = Substitute.For<ITicketNoticeSource>();
    private readonly IComplianceStore _compliance = Substitute.For<IComplianceStore>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetPlanNoticesHandlerTests()
    {
        _plans.ListRunsAsync(null, 1, Ct).Returns([]);
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([]);
        _journal.FillsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns([]);
        _journal.EntriesAsync(Arg.Any<long>(), Ct).Returns([]);
        _plans.HistoryAsync(Arg.Any<long>(), Ct).Returns([]);
        _tickets.ListAsync(Arg.Any<DateTimeOffset>(), Ct).Returns([]);
        _compliance.LatestPerKindAsync(Ct).Returns([]);
    }

    // Approved on the evening before the session, before any fill of the session day.
    private static PlanEventRow Approval(long item, DateTimeOffset? at = null) =>
        new(item, item, "proposed", "approved", "owner:me", "ok", 1, at ?? new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

    private GetPlanNoticesHandler Handler() => new(_plans, _journal, _tickets, _compliance, Substitute.For<XDrishti.Application.Abstractions.Messaging.IQueryHandler<XDrishti.Application.LiveTrading.GetLiveNoticesQuery, IReadOnlyList<XDrishti.Application.LiveTrading.LiveNoticeView>>>(), _time);

    private static PlanRow Row(long id, DateOnly? date = null, int version = 1, PlanState state = PlanState.Published) =>
        new(id, new NewPlan("k" + id, date ?? Session, Cutoff, version, Hash, "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []), state, 1, 0, 0);

    private static PlanItemRow Item(long id, long plan, TicketState state, TicketGroup group = TicketGroup.Primary, string symbol = "NSE:RELIANCE") =>
        new(id, plan, new NewPlanItem((int)id, group, symbol, "BUY", "ORB", Hash, "t", "s", "g", "v", [], new PlanEvidence("c", 30, "r", "b", null, null, [], null), [new NewAllocation(Account, "paper", 10)]), state);

    private static JournalFill Fill(string symbol = "RELIANCE", DateOnly? date = null, Guid? account = null) =>
        new(Guid.NewGuid(), account ?? Account, symbol, Side.Buy, date ?? Session, new TimeOnly(10, 0), 10, 100m, 0m);

    private void Store(PlanRow row, params PlanItemRow[] items)
    {
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([row]);
        _plans.ItemsAsync(row.Id, Ct).Returns(items);
    }

    private static StoredCompliance Compliance(string kind, string outcome, DateOnly next, long id = 1)
    {
        var decided = new DateOnly(2026, 6, 1);
        var draft = new ComplianceDraft(kind, outcome, decided, "noted", [], kind == ComplianceKinds.StaticIp ? "none" : null, null, null, null, null, next);
        return new StoredCompliance(id, ComplianceRecord.Create(draft, decided).Value, "owner:me", DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task A_compliance_record_whose_review_date_has_passed_is_one_overdue_notice_with_a_route_and_no_command_and_a_newer_record_resolves_it()
    {
        _compliance.LatestPerKindAsync(Ct).Returns([Compliance("sebi_recheck", "rechecked_no_change", new DateOnly(2026, 10, 11))]);

        var before = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);
        _compliance.LatestPerKindAsync(Ct).Returns([Compliance("sebi_recheck", "rechecked_no_change", new DateOnly(2027, 1, 9), id: 2)]);
        var after = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        var notice = before.Items.ShouldHaveSingleItem();
        notice.Kind.ShouldBe("compliance_recheck_overdue");
        notice.Text.ShouldContain("Compliance re-check overdue");
        notice.Text.ShouldContain("SEBI re-check");
        notice.Route.ShouldBe("/live?tab=compliance");
        notice.Id.ShouldBe("compliance:overdue:sebi_recheck:2026-10-11");
        after.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_review_date_of_today_is_not_yet_overdue_and_a_kind_with_no_record_has_no_notice()
    {
        _compliance.LatestPerKindAsync(Ct).Returns([Compliance("static_ip", "requested", new DateOnly(2026, 10, 12))]);

        (await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_stored_gives_an_empty_list()
    {
        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Undecided_tickets_of_a_published_plan_make_the_reminder_and_a_decision_removes_it()
    {
        Store(Row(1), Item(11, 1, TicketState.Proposed), Item(12, 1, TicketState.Modified), Item(13, 1, TicketState.Proposed, TicketGroup.Rejected));

        var before = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);
        _plans.ItemsAsync(1, Ct).Returns([Item(11, 1, TicketState.Approved), Item(12, 1, TicketState.Rejected)]);
        var after = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        before.Items.ShouldHaveSingleItem().Text.ShouldContain("2 tickets undecided"); // the rejected candidate is not a ticket
        after.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_newest_version_of_a_session_counts_and_old_sessions_are_not_read()
    {
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([Row(1, version: 1), Row(2, version: 2), Row(3, new DateOnly(2026, 10, 8))]);
        _plans.ItemsAsync(2, Ct).Returns([Item(21, 2, TicketState.Proposed)]);

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.ShouldHaveSingleItem().Id.ShouldBe("plan:review:2:info");
        await _plans.DidNotReceive().ItemsAsync(1, Ct);
        await _plans.DidNotReceive().ItemsAsync(3, Ct);
    }

    [Fact]
    public async Task A_failed_closed_run_inside_the_window_is_an_error_and_a_run_older_than_three_days_is_not()
    {
        var failed = new PlanRunRow(5, 1, new NewPlanRun(Session, null, "failed_closed", "eod", "the EOD run has not finished", null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        var old = new PlanRunRow(4, 1, failed.Run with { ForDate = new DateOnly(2026, 10, 8) });
        _plans.ListRunsAsync(null, 1, Ct).Returns([failed]);
        var shown = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);
        _plans.ListRunsAsync(null, 1, Ct).Returns([old]);
        var hidden = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        shown.Items.ShouldHaveSingleItem().Severity.ShouldBe("error");
        hidden.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lapsed_armed_and_invalidated_tickets_are_counted_by_state()
    {
        Store(Row(1, state: PlanState.Armed), Item(11, 1, TicketState.Lapsed), Item(12, 1, TicketState.Armed), Item(13, 1, TicketState.Armed, TicketGroup.Reserve), Item(14, 1, TicketState.Invalidated));

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.Select(i => i.Kind).ShouldBe(["plan_tickets_invalidated", "plan_tickets_lapsed", "plan_tickets_armed"]);
        result.Items.Single(i => i.Kind == "plan_tickets_armed").Text.ShouldStartWith("2 tickets");
    }

    [Fact]
    public async Task An_approved_ticket_with_a_fill_and_no_entry_is_raised_and_the_entry_resolves_it()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        Store(Row(1, state: PlanState.Expired), Item(11, 1, TicketState.Closed), Item(12, 1, TicketState.Closed, symbol: "NSE:TCS"), Item(13, 1, TicketState.Proposed, symbol: "NSE:INFY"));
        _plans.HistoryAsync(1, Ct).Returns([Approval(11), Approval(12)]);
        _journal.FillsAsync(Session, Session, Ct).Returns([Fill(), Fill("INFY")]); // INFY's ticket was never approved; TCS has no fill

        var raised = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);
        _journal.EntriesAsync(1, Ct).Returns([new StoredEntry(100, 1, 11, Account, Session, [], null)]);
        var matched = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        raised.Items.Single(i => i.Kind == "plan_fills_unmatched").Text.ShouldStartWith("1 ticket approved");
        matched.Items.ShouldNotContain(i => i.Kind == "plan_fills_unmatched");
    }

    [Fact]
    public async Task A_fill_of_another_account_or_another_day_does_not_count()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        Store(Row(1, state: PlanState.Expired), Item(11, 1, TicketState.Closed));
        _plans.HistoryAsync(1, Ct).Returns([Approval(11)]);
        _journal.FillsAsync(Session, Session, Ct).Returns([Fill(account: Guid.NewGuid()), Fill(date: Session.AddDays(1))]);

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.ShouldBeEmpty();
        await _journal.DidNotReceive().EntriesAsync(Arg.Any<long>(), Ct);
    }

    [Fact]
    public async Task Only_read_members_of_the_ports_are_called()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        Store(Row(1), Item(11, 1, TicketState.Approved));
        _plans.HistoryAsync(1, Ct).Returns([Approval(11)]);
        _journal.FillsAsync(Session, Session, Ct).Returns([Fill()]);

        await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        var calls = _plans.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Concat(_journal.ReceivedCalls().Select(c => c.GetMethodInfo().Name)).Distinct().ToList();
        calls.ShouldBeSubsetOf(["ListRunsAsync", "ListAsync", "ItemsAsync", "HistoryAsync", "FillsAsync", "EntriesAsync"]);
    }

    [Fact]
    public async Task A_fill_before_the_approval_is_the_owners_own_trade_and_raises_nothing()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        Store(Row(1, state: PlanState.Expired), Item(11, 1, TicketState.Closed));
        _plans.HistoryAsync(1, Ct).Returns([Approval(11, new DateTimeOffset(2026, 10, 13, 5, 0, 0, TimeSpan.Zero))]); // 10:30 India, after the 10:00 fill
        _journal.FillsAsync(Session, Session, Ct).Returns([Fill()]);

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_fill_that_fits_two_approved_tickets_is_said_not_to_be_linked_and_the_tickets_are_not_called_matchable()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        Store(Row(1, state: PlanState.Expired), Item(11, 1, TicketState.Closed), Item(12, 1, TicketState.Closed));
        _plans.HistoryAsync(1, Ct).Returns([Approval(11), Approval(12)]);
        _journal.FillsAsync(Session, Session, Ct).Returns([Fill()]);

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        var item = result.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe("plan:fills-ambiguous:1:1");
        item.Text.ShouldContain("fit more than one approved ticket");
        item.Text.ShouldContain("will not guess");
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task The_window_is_today_and_the_two_days_before_it(int daysAgo, bool shown)
    {
        var date = new DateOnly(2026, 10, 12).AddDays(-daysAgo);
        var cutoff = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero).AddMinutes(60); // still ahead of the clock
        var row = new PlanRow(1, new NewPlan("k", date, cutoff, 1, Hash, "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []), PlanState.Published, 1, 0, 0);
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([row]);
        _plans.ItemsAsync(1, Ct).Returns([Item(11, 1, TicketState.Proposed)]);
        var run = new PlanRunRow(5, 1, new NewPlanRun(date, null, "failed_closed", "eod", "r", null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        _plans.ListRunsAsync(null, 1, Ct).Returns([run]);

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.Select(i => i.Kind).Order().ShouldBe(shown ? ["plan_awaiting_review", "plan_nightly_failed"] : []);
    }

    [Theory]
    [InlineData(31, "plan_awaiting_review", "info")]
    [InlineData(30, "plan_awaiting_review", "warning")]
    [InlineData(1, "plan_awaiting_review", "warning")]
    [InlineData(0, null, null)]
    public async Task The_reminder_changes_at_thirty_minutes_and_is_gone_at_exactly_the_cutoff(int minutesLeft, string? kind, string? severity)
    {
        _time.SetUtcNow(Cutoff.AddMinutes(-minutesLeft));
        Store(Row(1), Item(11, 1, TicketState.Proposed));

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.Select(i => (i.Kind, i.Severity)).ToList().ShouldBe(kind is null ? [] : [(kind, severity!)]);
    }

    [Fact]
    public async Task Ticket_notices_of_the_source_are_carried_as_they_are_and_sorted_with_the_plan_items()
    {
        Store(Row(1), Item(1, 1, TicketState.Proposed));
        var crossed = new PlanNotice("crossing:5", "ticket_level_crossed", "warning", "crossed", "/plans?plan=1", 1, null, Cutoff, false);
        var prompt = new PlanNotice("prompt:2", "post_trade_prompt", "info", "ambiguous", "/plans?plan=1", 1, null, Cutoff, true);
        var clash = new PlanNotice("plan:review:1:info", "post_trade_prompt", "info", "same id", "/plans?plan=1", 1, null, null, false);
        _tickets.ListAsync(Arg.Any<DateTimeOffset>(), Ct).Returns([prompt, clash, crossed]);

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.Select(i => i.Id).ShouldBe(["crossing:5", "plan:review:1:info", "prompt:2"]);
        result.Items[0].ShouldBe(crossed);
        result.Items[2].Resolved.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_ticket_notices_the_answer_is_the_plan_items_alone()
    {
        Store(Row(1), Item(1, 1, TicketState.Proposed));

        var result = await Handler().HandleAsync(new GetPlanNoticesQuery(), Ct);

        result.Items.Select(i => i.Kind).ShouldBe(["plan_awaiting_review"]);
    }
}
