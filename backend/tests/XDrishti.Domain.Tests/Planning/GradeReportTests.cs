using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>
/// Spec P7-13 criterion 1: the top-1/3/5/all curves, the groups and the decision groups equal a table worked out by hand. Two plan-days (R; NT = not triggered, NG = not gradable; state):
/// <code>
/// day 1 (plan 1)  r1 primary +2 approved | r2 primary -1 rejected | r3 primary NT lapsed | r4 reserve +3 proposed | r5 rejected +1
/// day 2 (plan 2)  r1 primary -1 closed   | r2 primary +0.5 modified | r3 reserve -2 lapsed | r4 rejected -1 | r5 rejected NG
/// top-1: +2, -1  = 1 (2 triggered)       top-3: 1, -2.5 = -1.5 (5)       top-5 = top-all: 4, -2.5 = 1.5 (6)
/// primary 0.5 (4 of 5)  reserve 1 (2)  rejected 0 (2 of 3)   approved 1  modified 0.5  rejected -1  lapsed -2  undecided 3
/// </code>
/// </summary>
public sealed class GradeReportTests
{
    private const double Tolerance = 1e-9;

    private static readonly DateOnly D1 = new(2026, 3, 3);

    private static readonly DateOnly D2 = new(2026, 3, 4);

    private static GradedItem Item(long plan, DateOnly date, int rank, TicketGroup group, TicketState state, double? r, GradeStatus? status = null)
    {
        var s = status ?? (r is null ? GradeStatus.NotTriggered : GradeStatus.Graded);
        return new GradedItem(plan, date, rank, group, state, new TicketGrade(s, r, null, null, r is null ? null : "Target", null, null, "gross, no charges", "x"));
    }

    private static readonly GradedItem[] Fixture =
    [
        Item(1, D1, 1, TicketGroup.Primary, TicketState.Approved, 2),
        Item(1, D1, 2, TicketGroup.Primary, TicketState.Rejected, -1),
        Item(1, D1, 3, TicketGroup.Primary, TicketState.Lapsed, null),
        Item(1, D1, 4, TicketGroup.Reserve, TicketState.Proposed, 3),
        Item(1, D1, 5, TicketGroup.Rejected, TicketState.Proposed, 1),
        Item(2, D2, 1, TicketGroup.Primary, TicketState.Closed, -1),
        Item(2, D2, 2, TicketGroup.Primary, TicketState.Modified, 0.5),
        Item(2, D2, 3, TicketGroup.Reserve, TicketState.Lapsed, -2),
        Item(2, D2, 4, TicketGroup.Rejected, TicketState.Proposed, -1),
        Item(2, D2, 5, TicketGroup.Rejected, TicketState.Proposed, null, GradeStatus.NotGradable),
    ];

    [Fact]
    public void The_curves_equal_the_hand_computed_sums()
    {
        var report = GradeReport.Build(Fixture);

        report.Curves.Select(c => c.Name).ShouldBe(["top-1", "top-3", "top-5", "top-all"]);
        var top1 = report.Curves[0];
        top1.Points.Select(p => p.DayR).ShouldBe([2, -1]);
        top1.Points.Select(p => p.CumulativeR).ShouldBe([2, 1]);
        top1.Totals.TotalR.ShouldBe(1, Tolerance);
        top1.Totals.Triggered.ShouldBe(2);
        report.Curves[1].Points.Select(p => p.DayR).ShouldBe([1, -2.5]);
        report.Curves[1].Points.Select(p => p.CumulativeR).ShouldBe([1, -1.5]);
        report.Curves[1].Totals.Triggered.ShouldBe(5);
        report.Curves[2].Totals.TotalR.ShouldBe(1.5, Tolerance);
        report.Curves[3].Points.Select(p => p.DayR).ShouldBe([4, -2.5]);
        report.Curves[3].Totals.Triggered.ShouldBe(6);
        report.Curves[3].Totals.MeanR!.Value.ShouldBe(0.25, Tolerance);
        report.Curves[3].Totals.NotTriggered.ShouldBe(1);
    }

    [Fact]
    public void The_plan_groups_equal_the_hand_computed_figures()
    {
        var groups = GradeReport.Build(Fixture).Groups.ToDictionary(g => g.Name);

        groups["primary"].Tickets.ShouldBe(5);
        groups["primary"].Triggered.ShouldBe(4);
        groups["primary"].TotalR.ShouldBe(0.5, Tolerance);
        groups["primary"].MeanR!.Value.ShouldBe(0.125, Tolerance);
        groups["primary"].WinRate!.Value.ShouldBe(0.5, Tolerance);
        groups["reserve"].TotalR.ShouldBe(1, Tolerance);
        groups["reserve"].MeanR!.Value.ShouldBe(0.5, Tolerance);
        groups["rejected"].Tickets.ShouldBe(3);
        groups["rejected"].Triggered.ShouldBe(2);
        groups["rejected"].NotGradable.ShouldBe(1);
        groups["rejected"].TotalR.ShouldBe(0, Tolerance);
    }

    [Fact]
    public void The_decision_groups_read_the_newest_state_and_leave_out_rejected_candidates()
    {
        var decisions = GradeReport.Build(Fixture).Decisions.ToDictionary(g => g.Name);

        decisions["approved"].TotalR.ShouldBe(1, Tolerance);
        decisions["approved"].Triggered.ShouldBe(2);
        decisions["modified"].TotalR.ShouldBe(0.5, Tolerance);
        decisions["rejected"].TotalR.ShouldBe(-1, Tolerance);
        decisions["lapsed"].Tickets.ShouldBe(2);
        decisions["lapsed"].NotTriggered.ShouldBe(1);
        decisions["lapsed"].TotalR.ShouldBe(-2, Tolerance);
        decisions["undecided"].TotalR.ShouldBe(3, Tolerance);
        decisions.Values.Sum(d => d.Tickets).ShouldBe(7);
    }

    [Fact]
    public void The_comparisons_say_who_did_better_and_never_claim_significance()
    {
        var comparisons = GradeReport.Build(Fixture).Comparisons;

        comparisons[0].MeanDifference!.Value.ShouldBe(0.375, Tolerance);
        comparisons[0].Verdict.ShouldBe("first_better");
        comparisons[1].MeanDifference!.Value.ShouldBe(-0.125, Tolerance);
        comparisons[1].Verdict.ShouldBe("second_better");
        comparisons[2].MeanDifference!.Value.ShouldBe(1.5, Tolerance);
        comparisons[3].MeanDifference!.Value.ShouldBe(2.5, Tolerance);
        comparisons.ShouldAllBe(c => c.Note.Contains("indicative only", StringComparison.Ordinal));
    }

    [Fact]
    public void Small_samples_carry_a_note_and_an_empty_set_is_not_enough_data()
    {
        var report = GradeReport.Build([]);

        report.Plans.ShouldBe(0);
        report.Curves.ShouldAllBe(c => c.Totals.MeanR == null && c.Totals.Note!.StartsWith("indicative only", StringComparison.Ordinal));
        report.Comparisons.ShouldAllBe(c => c.Verdict == "not_enough_data");
        report.Basis.ShouldBe("simulated on plan prices");
    }

    [Fact]
    public void The_same_items_give_the_same_report_in_any_order()
    {
        var a = GradeReport.Build(Fixture);
        var b = GradeReport.Build([.. Fixture.Reverse()]);

        b.Curves.Select(c => c.Totals).ShouldBe(a.Curves.Select(c => c.Totals));
        b.Decisions.ShouldBe(a.Decisions);
    }

    [Fact]
    public void The_notes_give_the_not_gradable_share_with_the_top_reasons_the_default_fill_rules_and_a_truncation_flag()
    {
        var report = GradeReport.Build(Fixture, truncated: true);

        report.Notes.ShouldContain(n => n.StartsWith("Not gradable: 1 of 10 tickets; top reasons: 1 x x", StringComparison.Ordinal));
        report.Notes.ShouldContain(n => n.Contains("default fill rules", StringComparison.Ordinal) && n.Contains("original plan prices", StringComparison.Ordinal));
        report.Notes.ShouldContain(n => n.StartsWith("TRUNCATED", StringComparison.Ordinal));
        GradeReport.Build(Fixture).Notes.ShouldNotContain(n => n.StartsWith("TRUNCATED", StringComparison.Ordinal));
    }

    [Fact]
    public void A_ticket_state_maps_to_one_decision_group()
    {
        DecisionGroups.Of(TicketState.Armed).ShouldBe(DecisionGroup.Approved);
        DecisionGroups.Of(TicketState.Expired).ShouldBe(DecisionGroup.Approved);
        DecisionGroups.Of(TicketState.Proposed).ShouldBe(DecisionGroup.Undecided);
        Enum.GetValues<TicketState>().Select(DecisionGroups.Of).Distinct().Count().ShouldBe(5);
    }
}
