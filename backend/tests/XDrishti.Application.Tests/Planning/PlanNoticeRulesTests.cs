using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P8-15: the plan items of the bell are a pure function of the stored state and the clock. Each appears, resolves by itself when the state changes, is ordered worst first, and carries a route, no command and no strategy text.</summary>
public sealed class PlanNoticeRulesTests
{
    private static readonly DateOnly Session = new(2026, 10, 13);

    // 2026-10-13 09:05 India.
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 13, 3, 35, 0, TimeSpan.Zero);

    private static DateTimeOffset MinutesBeforeCutoff(double minutes) => Cutoff.AddMinutes(-minutes);

    private static PlanNoticePlan Plan(long id = 7, PlanState state = PlanState.Published, int undecided = 0, int lapsed = 0, int armed = 0, int invalidated = 0, int fills = 0, DateOnly? date = null, DateTimeOffset? cutoff = null) =>
        new(id, date ?? Session, cutoff ?? Cutoff, state, undecided, lapsed, armed, invalidated, fills);

    private static PlanNoticeRun Run(string outcome = "failed_closed", string? gate = "eod", string reason = "the EOD run has not finished for 2026-10-12", DateOnly? date = null) =>
        new(date ?? Session, outcome, gate, reason, new DateTimeOffset(2026, 10, 12, 13, 5, 0, TimeSpan.Zero));

    private static IReadOnlyList<PlanNotice> Derive(DateTimeOffset now, PlanNoticePlan[]? plans = null, PlanNoticeRun? run = null) => PlanNoticeRules.Derive(now, plans ?? [], run);

    [Fact]
    public void Nothing_stored_gives_no_items()
    {
        Derive(MinutesBeforeCutoff(600)).ShouldBeEmpty();
    }

    [Fact]
    public void A_plan_with_undecided_tickets_before_the_cutoff_says_how_many_minutes_remain_and_leads_to_the_plan()
    {
        var items = Derive(MinutesBeforeCutoff(125.5), [Plan(undecided: 3)]);

        var item = items.ShouldHaveSingleItem();
        item.Kind.ShouldBe("plan_awaiting_review");
        item.Id.ShouldBe("plan:review:7");
        item.Severity.ShouldBe("info");
        item.Route.ShouldBe("/plans?plan=7");
        item.PlanId.ShouldBe(7);
        item.At.ShouldBe(Cutoff);
        item.Text.ShouldBe("The plan for 13 Oct awaits your review: 3 tickets undecided, 126 minutes to the cutoff at 09:05. Unreviewed tickets lapse.");
    }

    [Theory]
    [InlineData(31, "info")]
    [InlineData(30, "warning")]
    [InlineData(1, "warning")]
    public void The_reminder_turns_into_a_warning_at_thirty_minutes(double minutes, string severity)
    {
        Derive(MinutesBeforeCutoff(minutes), [Plan(state: PlanState.InReview, undecided: 1)]).Single().Severity.ShouldBe(severity);
    }

    [Fact]
    public void One_minute_and_one_ticket_are_singular()
    {
        Derive(MinutesBeforeCutoff(0.5), [Plan(undecided: 1)]).Single().Text.ShouldContain("1 ticket undecided, 1 minute to the cutoff");
    }

    [Fact]
    public void The_reminder_resolves_when_every_ticket_is_decided_the_cutoff_passes_or_the_plan_moves_on()
    {
        var now = MinutesBeforeCutoff(60);

        Derive(now, [Plan(state: PlanState.InReview, undecided: 0)]).ShouldBeEmpty();
        Derive(Cutoff, [Plan(undecided: 2)]).ShouldBeEmpty();
        Derive(Cutoff.AddMinutes(5), [Plan(undecided: 2)]).ShouldBeEmpty();
        Derive(now, [Plan(state: PlanState.Lapsed, undecided: 2)]).ShouldBeEmpty();
        Derive(now, [Plan(state: PlanState.Approved, undecided: 2)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_nightly_run_that_failed_closed_names_the_gate_and_the_reason_and_has_no_plan_to_open()
    {
        var item = Derive(MinutesBeforeCutoff(600), run: Run()).ShouldHaveSingleItem();

        item.Kind.ShouldBe("plan_nightly_failed");
        item.Id.ShouldBe("plan:nightly:2026-10-13");
        item.Severity.ShouldBe("error");
        item.Route.ShouldBe("/plans");
        item.PlanId.ShouldBeNull();
        item.Text.ShouldBe("The nightly plan for 13 Oct failed closed at the eod gate: the EOD run has not finished for 2026-10-12. No plan was published.");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("dry_run")]
    public void A_run_that_published_or_was_a_dry_run_raises_nothing(string outcome)
    {
        Derive(MinutesBeforeCutoff(600), run: Run(outcome, gate: null)).ShouldBeEmpty();
    }

    [Fact]
    public void A_long_reason_is_cut_to_one_line_and_a_missing_one_is_said()
    {
        var cut = Derive(MinutesBeforeCutoff(600), run: Run(reason: new string('x', 400) + "\nsecond line")).Single().Text;
        var none = Derive(MinutesBeforeCutoff(600), run: Run(gate: null, reason: " ")).Single().Text;

        cut.ShouldNotContain("\n");
        cut.Length.ShouldBeLessThan(260);
        none.ShouldBe("The nightly plan for 13 Oct failed closed at a gate: see the plan runs. No plan was published.");
    }

    [Fact]
    public void Lapsed_tickets_warn_that_lapsing_is_never_approval_until_the_session_is_over()
    {
        var plan = Plan(state: PlanState.Lapsed, lapsed: 2);

        var item = Derive(MinutesBeforeCutoff(-60), [plan]).ShouldHaveSingleItem();
        item.Kind.ShouldBe("plan_tickets_lapsed");
        item.Severity.ShouldBe("warning");
        item.Text.ShouldBe("2 tickets of the plan for 13 Oct lapsed: the cutoff passed with no decision. Lapsing is never approval.");

        Derive(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero), [plan]).ShouldHaveSingleItem(); // 23:30 India on the session day: still shown (ForDate is today)
        Derive(new DateTimeOffset(2026, 10, 13, 18, 31, 0, TimeSpan.Zero), [plan]).ShouldBeEmpty(); // 00:01 on the 14th: over
        Derive(MinutesBeforeCutoff(-60), [plan with { State = PlanState.Expired }]).ShouldBeEmpty();
    }

    [Fact]
    public void Armed_and_invalidated_tickets_are_separate_items_and_each_goes_when_its_count_is_zero()
    {
        var both = Derive(MinutesBeforeCutoff(-30), [Plan(state: PlanState.Armed, armed: 2, invalidated: 1)]);

        both.Select(i => (i.Kind, i.Severity)).ShouldBe([("plan_tickets_invalidated", "warning"), ("plan_tickets_armed", "info")]);
        both.Single(i => i.Kind == "plan_tickets_armed").Text.ShouldBe("2 tickets of the plan for 13 Oct armed at the pre-open check: watched, no order is placed.");
        both.Single(i => i.Kind == "plan_tickets_invalidated").Text.ShouldContain("1 ticket of the plan for 13 Oct invalidated at the pre-open check");

        Derive(MinutesBeforeCutoff(-30), [Plan(state: PlanState.Armed, armed: 0, invalidated: 1)]).Select(i => i.Kind).ShouldBe(["plan_tickets_invalidated"]);
        Derive(MinutesBeforeCutoff(-30), [Plan(state: PlanState.Expired, armed: 1)]).ShouldBeEmpty();
    }

    [Fact]
    public void Approved_tickets_with_a_fill_and_no_journal_entry_are_one_information_item_that_goes_when_matched()
    {
        var now = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

        var item = Derive(now, [Plan(state: PlanState.Expired, fills: 2)]).ShouldHaveSingleItem();
        item.Kind.ShouldBe("plan_fills_unmatched");
        item.Id.ShouldBe("plan:fills:7");
        item.Severity.ShouldBe("info");
        item.Text.ShouldBe("2 tickets approved for 13 Oct have a fill from the Dhan sync that is not matched yet. The journal matches it to the ticket.");

        Derive(now, [Plan(state: PlanState.Expired, fills: 0)]).ShouldBeEmpty();
    }

    [Fact]
    public void Items_are_ordered_error_then_warning_then_information_newer_session_first_then_by_id()
    {
        var older = new DateOnly(2026, 10, 12);
        var now = MinutesBeforeCutoff(-30);
        var plans = new[]
        {
            Plan(id: 2, state: PlanState.Expired, fills: 1, date: older),
            Plan(id: 3, state: PlanState.Armed, armed: 1, invalidated: 1),
            Plan(id: 1, state: PlanState.Lapsed, lapsed: 1, date: Session),
        };

        var items = Derive(now, plans, Run());

        items.Select(i => i.Id).ShouldBe(["plan:nightly:2026-10-13", "plan:invalidated:3", "plan:lapsed:1", "plan:armed:3", "plan:fills:2"]);
    }

    [Fact]
    public void Every_item_has_an_in_app_route_and_no_text_names_a_symbol_a_size_or_a_command()
    {
        var items = Derive(MinutesBeforeCutoff(10), [Plan(undecided: 2, lapsed: 1, armed: 1, invalidated: 1, fills: 1)], Run());

        items.Count.ShouldBe(6);
        items.ShouldAllBe(i => i.Route.StartsWith("/plans", StringComparison.Ordinal));
        items.ShouldAllBe(i => !i.Text.Contains("RELIANCE", StringComparison.Ordinal) && !i.Text.Contains("POST ", StringComparison.Ordinal));
        typeof(PlanNotice).GetProperties().Select(p => p.Name).ShouldBe(["Id", "Kind", "Severity", "Text", "Route", "PlanId", "At"], ignoreOrder: true); // no action or command member
    }
}
