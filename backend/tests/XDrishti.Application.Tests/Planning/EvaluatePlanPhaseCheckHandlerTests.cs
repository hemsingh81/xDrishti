#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Planning;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>
/// Spec P7-18 over fake stores: the window is the last ten trading sessions of the calendar (a holiday between two sessions is skipped, not a gap), a manual run, a failed-closed attempt or a day with no run breaks the run, ten consecutive scheduled sessions meet the exit,
/// the default end of the window waits for the nightly time, the review, evidence and allow-list facts are read from the stores, the same facts give the same text, and nothing is written.
/// </summary>
public sealed class EvaluatePlanPhaseCheckHandlerTests
{
    // Friday 2026-10-23, 20:00 India time: the nightly (18:30) has run for the planned session Monday 2026-10-26.
    private static readonly DateTimeOffset Evening = new(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly Friday = new(2026, 10, 23);

    private static readonly DateOnly Monday = new(2026, 10, 26);

    private readonly List<PlanRunRow> _runs = [];

    private readonly Dictionary<long, PlanRow> _planRows = [];

    private readonly Dictionary<long, List<PlanItemRow>> _items = [];

    private readonly Dictionary<long, List<ApprovalRow>> _approvals = [];

    private readonly Dictionary<long, StoredGrade> _grades = [];

    private readonly Dictionary<long, List<StoredEntry>> _entries = [];

    private readonly IPlanStore _store = Substitute.For<IPlanStore>();

    private readonly IGradeStore _gradeStore = Substitute.For<IGradeStore>();

    private readonly IJournalStore _journal = Substitute.For<IJournalStore>();

    private readonly IDigestStore _digests = Substitute.For<IDigestStore>();

    private readonly IAutonomySettingsStore _autonomy = Substitute.For<IAutonomySettingsStore>();

    private readonly IServiceScheduleReader _services = Substitute.For<IServiceScheduleReader>();

    private readonly IBrokerAllowList _allowList = Substitute.For<IBrokerAllowList>();

    private readonly IPlanBuilderSettings _builder = Substitute.For<IPlanBuilderSettings>();

    private readonly IOpenBookSettings _openBook = Substitute.For<IOpenBookSettings>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private FakeTimeProvider _time = new(Evening);

    private long _nextPlan = 1;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public EvaluatePlanPhaseCheckHandlerTests()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _store.ListRunsAsync(Arg.Any<DateOnly?>(), Arg.Any<int>(), Ct).Returns(call =>
            ValueTask.FromResult<IReadOnlyList<PlanRunRow>>([.. _runs.Where(r => call.Arg<DateOnly?>() is not { } d || r.Run.ForDate == d).OrderByDescending(r => r.Id).Take(call.Arg<int>())]));
        _store.FindAsync(Arg.Any<long>(), Ct).Returns(call => ValueTask.FromResult(_planRows.GetValueOrDefault(call.Arg<long>())));
        _store.ListAsync(null, null, Arg.Any<int>(), Ct).Returns(_ => ValueTask.FromResult<IReadOnlyList<PlanRow>>([.. _planRows.Values.OrderByDescending(p => p.Id)]));
        _store.ItemsAsync(Arg.Any<long>(), Ct).Returns(call => ValueTask.FromResult<IReadOnlyList<PlanItemRow>>([.. _items.GetValueOrDefault(call.Arg<long>()) ?? []]));
        _store.ApprovalsAsync(Arg.Any<long>(), Ct).Returns(call => ValueTask.FromResult<IReadOnlyList<ApprovalRow>>([.. _approvals.GetValueOrDefault(call.Arg<long>()) ?? []]));
        _gradeStore.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(_ => ValueTask.FromResult<IReadOnlyDictionary<long, StoredGrade>>(_grades));
        _journal.EntriesAsync(Arg.Any<long>(), Ct).Returns(call => ValueTask.FromResult<IReadOnlyList<StoredEntry>>([.. _entries.GetValueOrDefault(call.Arg<long>()) ?? []]));
        _digests.ListAsync(Arg.Any<int>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<StoredDigest>>([]));
        _autonomy.CurrentAsync(Ct).Returns(AutonomySettings.Defaults);
        _autonomy.HistoryAsync(Arg.Any<int>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<AutonomyChangeRow>>([]));
        _services.GetAsync(Arg.Any<string>(), Ct).Returns(ValueTask.FromResult<ServiceScheduleInfo?>(new ServiceScheduleInfo(false, new TimeOnly(18, 30), "daily", null)));
        _allowList.Entries().Returns(PlanPhaseCheck.PinnedAllowList);
        _allowList.Allows(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
    }

    private EvaluatePlanPhaseCheckHandler Handler() =>
        new(_calendar, _store, _gradeStore, _journal, _digests, _autonomy, _services, _allowList, _builder, _openBook, _time, NullLogger<EvaluatePlanPhaseCheckHandler>.Instance);

    private async Task<PlanPhaseReport> Run(DateOnly? through = null)
    {
        var result = await Handler().HandleAsync(new EvaluatePlanPhaseCheckQuery(through), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value.Report;
    }

    /// <summary>The ten trading sessions up to a date on the plain calendar (weekends skipped), newest first.</summary>
    private static List<DateOnly> Sessions(DateOnly through, Func<DateOnly, bool>? isHoliday = null)
    {
        var dates = new List<DateOnly>();
        for (var d = through; dates.Count < 10; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !(isHoliday?.Invoke(d) ?? false))
            {
                dates.Add(d);
            }
        }

        return dates;
    }

    private long Publish(DateOnly forDate, bool manual = false, int primary = 2, string basis = "cell statistics only", NewPlan? plan = null)
    {
        var id = _nextPlan++;
        var at = new DateTimeOffset(forDate.AddDays(-1).ToDateTime(new TimeOnly(18, 30)), TimeSpan.FromMinutes(330)).ToUniversalTime();
        _planRows[id] = new PlanRow(id, plan ?? NewPlanFor(forDate, basis), PlanState.Published, primary, 1, 3);
        _runs.Add(new PlanRunRow(_runs.Count + 1, _runs.Count(r => r.Run.ForDate == forDate) + 1, new NewPlanRun(forDate, "k" + id, "published", null, "Published", id, manual, at, at.AddMinutes(1))));
        return id;
    }

    private void Fail(DateOnly forDate, string gate = "eod", bool manual = false)
    {
        var at = new DateTimeOffset(forDate.AddDays(-1).ToDateTime(new TimeOnly(18, 30)), TimeSpan.FromMinutes(330)).ToUniversalTime();
        _runs.Add(new PlanRunRow(_runs.Count + 1, _runs.Count(r => r.Run.ForDate == forDate) + 1, new NewPlanRun(forDate, null, "failed_closed", gate, "the end-of-day run had not finished", null, manual, at, at.AddMinutes(1))));
    }

    private void PublishAll(IEnumerable<DateOnly> dates, int primary = 2)
    {
        foreach (var date in dates)
        {
            Publish(date, primary: primary);
        }
    }

    private static NewPlan NewPlanFor(DateOnly forDate, string basis) =>
        new("nightly:" + forDate.ToString("yyyy-MM-dd"), forDate, new DateTimeOffset(forDate.ToDateTime(new TimeOnly(9, 5)), TimeSpan.FromMinutes(330)).ToUniversalTime(), 1, new string('b', 64), basis, true, "system:nightly", DateTimeOffset.UnixEpoch, []);

    private static PlanItemRow Item(long id, long planId, TicketState state, TicketGroup group = TicketGroup.Primary, string mode = "paper", string dataRange = "about 2.1 years, replayed history") =>
        new(id, planId, new NewPlanItem((int)id, group, "ABC", "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", group == TicketGroup.Primary ? [] : ["why"], new PlanEvidence("c", 30, dataRange, "cell statistics lower bound", 0.6, null, [], null), [new NewAllocation(Guid.NewGuid(), mode, 10)]), state);

    [Fact]
    public async Task Ten_consecutive_scheduled_sessions_with_a_holiday_between_meet_the_exit_and_the_holiday_is_not_read()
    {
        var holiday = new DateOnly(2026, 10, 15);
        _calendar.GetAsync(Ct).Returns(new TradingCalendar([TradingDay.Holiday(holiday, CalendarSource.Manual, CalendarStatus.Confirmed, null, Evening)]));
        var window = Sessions(Monday, d => d == holiday);
        PublishAll(window);

        var report = await Run();

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.Met);
        report.Streak.ShouldBe(10);
        report.Through.ShouldBe(Monday);
        report.Sessions.Select(s => s.Date).ShouldBe(window.AsEnumerable().Reverse());
        report.Sessions.Select(s => s.Date).ShouldNotContain(holiday);
        await _store.DidNotReceive().ListRunsAsync(holiday, Arg.Any<int>(), Ct);
    }

    [Fact]
    public async Task A_manual_run_in_the_window_breaks_it()
    {
        var window = Sessions(Monday);
        PublishAll(window.Skip(1));
        Publish(window[0], manual: true);

        var report = await Run();

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Sessions[^1].State.ShouldBe("manual_run");
        report.Streak.ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_closed_day_breaks_it_and_names_its_gate()
    {
        var window = Sessions(Monday);
        PublishAll(window.Where((_, i) => i != 3));
        Fail(window[3], "data-quality");

        var report = await Run();

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Sessions.Single(s => s.State == "failed_closed").Gates.ShouldBe("4 pass, data-quality did not pass");
        report.Criteria.Single(c => c.Number == 2).Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable); // one gate seen, five not yet
        report.Criteria.Single(c => c.Number == 2).Detail.ShouldContain("data-quality 1");
    }

    [Fact]
    public async Task A_day_with_no_run_breaks_it()
    {
        var window = Sessions(Monday);
        PublishAll(window.Where((_, i) => i != 5));

        var report = await Run();

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Sessions.Single(s => s.State == "no_run").Date.ShouldBe(window[5]);
    }

    [Fact]
    public async Task Fewer_than_ten_recorded_sessions_are_not_evaluable_never_met()
    {
        PublishAll(Sessions(Monday).Take(4));

        var report = await Run();

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        report.Streak.ShouldBe(4);
        report.Sessions.Count(s => s.State == "before_history").ShouldBe(6);
    }

    [Fact]
    public async Task An_empty_database_is_not_evaluable_and_the_limits_are_still_printed()
    {
        var result = await Handler().HandleAsync(new EvaluatePlanPhaseCheckQuery(null), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        result.Value.Report.Sessions.ShouldAllBe(s => s.State == "before_history");
        result.Value.Text.ShouldContain("limit: about 2.1 years of data");
        result.Value.Text.ShouldContain("count plans read: 0");
    }

    [Fact]
    public async Task Before_the_nightly_time_the_session_not_yet_due_is_not_read_as_a_gap()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)); // 17:30 India time
        PublishAll(Sessions(Friday));

        var report = await Run();

        report.Through.ShouldBe(Friday);
        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.Met);
    }

    [Fact]
    public async Task After_the_nightly_time_the_same_missing_run_is_a_gap()
    {
        PublishAll(Sessions(Friday));

        var report = await Run();

        report.Through.ShouldBe(Monday);
        report.Sessions[^1].State.ShouldBe("no_run");
        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
    }

    [Fact]
    public async Task A_run_row_for_the_newest_session_makes_it_due_even_early()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        Fail(Monday, "eod");

        var report = await Run();

        report.Through.ShouldBe(Monday);
    }

    [Fact]
    public async Task An_explicit_date_moves_the_window_and_a_weekend_date_takes_the_session_before()
    {
        PublishAll(Sessions(Friday));

        var report = await Run(new DateOnly(2026, 10, 24));

        report.Through.ShouldBe(Friday);
        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.Met);
    }

    [Fact]
    public async Task A_date_after_the_newest_planned_session_is_refused()
    {
        var result = await Handler().HandleAsync(new EvaluatePlanPhaseCheckQuery(new DateOnly(2026, 10, 27)), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PlanErrors.InvalidCode);
    }

    [Fact]
    public async Task The_check_only_reads_and_writes_nothing()
    {
        var window = Sessions(Monday);
        PublishAll(window);
        var id = _planRows.Keys.First();
        _items[id] = [Item(1, id, TicketState.Approved)];

        await Run();

        string[] reads = ["ListRunsAsync", "ListAsync", "FindAsync", "ItemsAsync", "ApprovalsAsync", "ForPlansAsync", "EntriesAsync", "CurrentAsync", "HistoryAsync", "GetAsync", "Entries", "Allows", "get_UseSelection", "get_AssumedEmpty"];
        foreach (var fake in new object[] { _store, _gradeStore, _journal, _digests, _autonomy, _services, _allowList, _builder, _openBook })
        {
            fake.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Distinct().ShouldBeSubsetOf(reads);
        }
    }

    [Fact]
    public async Task The_same_stored_facts_give_the_same_text()
    {
        PublishAll(Sessions(Monday));

        var first = (await Handler().HandleAsync(new EvaluatePlanPhaseCheckQuery(null), Ct)).Value.Text;
        var second = (await Handler().HandleAsync(new EvaluatePlanPhaseCheckQuery(null), Ct)).Value.Text;

        second.ShouldBe(first);
    }

    [Fact]
    public async Task Services_and_switches_are_read_and_the_ones_still_off_are_the_blockers()
    {
        _services.GetAsync("nightly", Ct).Returns(ValueTask.FromResult<ServiceScheduleInfo?>(new ServiceScheduleInfo(false, new TimeOnly(18, 30), "daily", null)));
        _services.GetAsync("eod", Ct).Returns(ValueTask.FromResult<ServiceScheduleInfo?>(new ServiceScheduleInfo(true, new TimeOnly(15, 50), "daily", null)));
        _services.GetAsync("weekly", Ct).Returns(ValueTask.FromResult<ServiceScheduleInfo?>(null));
        _builder.UseSelection.Returns(true);
        _openBook.AssumedEmpty.Returns(false);
        _autonomy.CurrentAsync(Ct).Returns(new AutonomySettings(3, new string('a', 64), true, false, 0.58, 3));

        var report = await Run();

        report.Services.Single(s => s.Id == "nightly").State.ShouldBe("paused");
        report.Services.Single(s => s.Id == "eod").State.ShouldBe("enabled");
        report.Services.Single(s => s.Id == "weekly").State.ShouldBe("not registered");
        report.Switches.Single(s => s.Name == "Planning:Builder:UseSelection").On.ShouldBeTrue();
        report.Switches.Single(s => s.Name == "Planning:OpenBook:AssumedEmpty").On.ShouldBeFalse();
        report.Switches.Single(s => s.Name == AutonomySettings.AutoApproveKey).On.ShouldBeTrue();
        report.Blockers.ShouldContain(b => b.StartsWith("service nightly is paused", StringComparison.Ordinal));
        report.Blockers.ShouldContain(b => b.StartsWith("switch Planning:OpenBook:AssumedEmpty is OFF", StringComparison.Ordinal));
        report.Blockers.ShouldNotContain(b => b.Contains("UseSelection", StringComparison.Ordinal));
        report.Blockers.ShouldNotContain(b => b.Contains("service eod", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_auto_approval_with_the_switch_never_on_is_not_met_and_with_it_on_in_the_history_is_met()
    {
        var id = Publish(Monday);
        _items[id] = [Item(1, id, TicketState.Approved)];
        _approvals[id] = [new ApprovalRow(1, 1, "auto", "approve", "rule:autonomy", "auto-approved", null, null, new DateTimeOffset(2026, 10, 26, 3, 0, 0, TimeSpan.Zero))];

        var off = await Run();
        _autonomy.HistoryAsync(Arg.Any<int>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<AutonomyChangeRow>>([new AutonomyChangeRow(1, new string('a', 64), true, false, 0.58, 3, "owner:x", "on", Evening)]));
        var on = await Run();

        off.Criteria.Single(c => c.Number == 7).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        off.Criteria.Single(c => c.Number == 7).Detail.ShouldContain("switch was never stored as ON");
        on.Criteria.Single(c => c.Number == 7).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
    }

    [Fact]
    public async Task Owner_decisions_before_the_cutoff_count_and_a_decision_after_it_is_not_met()
    {
        var id = Publish(Monday);
        _items[id] = [Item(1, id, TicketState.Approved), Item(2, id, TicketState.Rejected), Item(3, id, TicketState.Lapsed)];
        var before = new DateTimeOffset(2026, 10, 26, 3, 0, 0, TimeSpan.Zero); // 08:30 India time, cutoff 09:05
        _approvals[id] = [new ApprovalRow(1, 1, "owner", "approve", "owner:x", "ok", null, null, before), new ApprovalRow(2, 2, "owner", "reject", "owner:x", "no", null, null, before)];

        var fine = await Run();
        _approvals[id].Add(new ApprovalRow(3, 3, "owner", "modify", "owner:x", "late", null, "qty", before.AddHours(2)));
        var late = await Run();

        fine.Criteria.Single(c => c.Number == 7).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        fine.Criteria.Single(c => c.Number == 7).Detail.ShouldContain("owner approved 1, modified 0, rejected 1, auto approved 0, lapsed 1");
        late.Criteria.Single(c => c.Number == 7).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        late.Criteria.Single(c => c.Number == 7).Detail.ShouldContain("1 decision(s) dated at or after the cutoff");
    }

    [Fact]
    public async Task An_armed_ticket_with_no_approve_row_and_a_ticket_left_undecided_long_after_the_cutoff_are_not_met()
    {
        var id = Publish(new DateOnly(2026, 10, 21)); // cutoff Wednesday 09:05, long past on Friday evening
        _items[id] = [Item(1, id, TicketState.Armed), Item(2, id, TicketState.Proposed), Item(3, id, TicketState.Proposed, TicketGroup.Rejected)];

        var report = await Run();

        var criterion = report.Criteria.Single(c => c.Number == 7);
        criterion.Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        criterion.Detail.ShouldContain("1 approved or armed ticket(s) without an approve row");
        criterion.Detail.ShouldContain("1 ticket(s) still undecided well after the cutoff");
        report.Counts.Single(c => c.Name == "armed tickets").Value.ShouldBe(1);
    }

    [Fact]
    public async Task Evidence_and_allocations_are_read_from_the_stored_tickets()
    {
        var id = Publish(Monday);
        _items[id] = [Item(1, id, TicketState.Proposed), Item(2, id, TicketState.Proposed, TicketGroup.Reserve)];
        var good = await Run();
        _items[id] = [Item(1, id, TicketState.Proposed, dataRange: " "), Item(2, id, TicketState.Proposed, TicketGroup.Reserve, mode: "manual_live")];
        var bad = await Run();

        good.Criteria.Single(c => c.Number == 6).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        good.Criteria.Single(c => c.Number == 8).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        bad.Criteria.Single(c => c.Number == 6).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        bad.Criteria.Single(c => c.Number == 8).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
    }

    [Fact]
    public async Task A_plan_that_lost_its_no_order_sentence_is_not_met()
    {
        Publish(Monday, plan: NewPlanFor(Monday, "x") with { Notice = "changed" });

        var report = await Run();

        report.Criteria.Single(c => c.Number == 6).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
    }

    [Fact]
    public async Task Grades_and_journal_are_counted_and_a_fill_linked_twice_is_not_met()
    {
        var id = Publish(Monday);
        _items[id] = [Item(1, id, TicketState.Closed), Item(2, id, TicketState.Closed, TicketGroup.Reserve)];
        var grade = new TicketGrade(GradeStatus.Graded, 1.0, null, null, "Target", null, null, "gross, no charges", "x");
        _grades[1] = new StoredGrade(1, new NewGrade(id, 1, Monday, grade, "run", Evening));
        var fill = new StoredFill(Guid.NewGuid(), Guid.NewGuid(), "ABC", "buy", Monday, null, 10, 100m, 1m, "entry", 10, "matched");
        _entries[id] = [new StoredEntry(1, id, 1, Guid.NewGuid(), Monday, [fill], null)];

        var once = await Run();
        _entries[id] = [new StoredEntry(1, id, 1, Guid.NewGuid(), Monday, [fill], null), new StoredEntry(2, id, 2, Guid.NewGuid(), Monday, [fill], null)];
        var twice = await Run();

        once.Criteria.Single(c => c.Number == 9).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        once.Criteria.Single(c => c.Number == 9).Detail.ShouldContain("1 of 2 tickets");
        once.Criteria.Single(c => c.Number == 11).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        twice.Criteria.Single(c => c.Number == 11).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        once.Counts.Single(c => c.Name == "graded tickets").Value.ShouldBe(1);
    }

    [Fact]
    public async Task An_allow_list_that_would_allow_an_order_request_is_not_met()
    {
        _allowList.Allows("POST", "https://api.dhan.co/v2/orders").Returns(true);

        var report = await Run();

        report.Criteria.Single(c => c.Number == 4).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Criteria.Single(c => c.Number == 4).Detail.ShouldContain("POST https://api.dhan.co/v2/orders");
    }

    [Fact]
    public async Task Failed_closed_rows_are_counted_per_gate_from_all_the_rows_read()
    {
        Fail(new DateOnly(2026, 9, 1), "eod");
        Fail(new DateOnly(2026, 9, 2), "eod");
        Fail(new DateOnly(2026, 9, 3), "fresh-data");

        var report = await Run();

        report.Criteria.Single(c => c.Number == 2).Detail.ShouldContain("per gate: eod 2, fresh-data 1");
    }
}
