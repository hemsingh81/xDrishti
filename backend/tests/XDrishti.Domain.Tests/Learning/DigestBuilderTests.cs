using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Spec P7-15: the weekly digest is exactly the hand-checked text for a fixture of stored records; the same input gives the same text; every section says nothing was applied.</summary>
public sealed class DigestBuilderTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static DigestWeek Week => DigestWeek.TryParse("2026-W43")!.Value;

    private static readonly DateTimeOffset Saturday = new(2026, 10, 24, 10, 0, 0, TimeSpan.FromMinutes(330));

    private static DigestInput Fixture() => new(
        Week,
        Saturday,
        new DateOnly(2026, 10, 23),
        [new DigestItem(7, "transition", Cell, "research -> candidate", "enough trades")],
        [new DigestItem(3, "cusum", Cell, "open", "mean R fell")],
        [],
        [],
        2,
        1,
        [new DigestItem(5, "hypothesis", string.Empty, "judged", "slippage 1 -> 2 (experiment #4): fills dearer")],
        1,
        [new DigestItem(4, "experiment", "slippage", "Better", "better in 3 of 3 folds (3 needed); 1 trials in 30 days")],
        null,
        [new DigestItem(2, "champion", string.Empty, "Eligible", "beat both baselines in 3 of 3 folds (3 needed)")],
        [
            new PhaseCriterion(1, "beats baselines", PhaseVerdict.NotEvaluable, "only 3 folds"),
            new PhaseCriterion(2, "calibration", PhaseVerdict.Met, "1.20 pp"),
            new PhaseCriterion(3, "replay time", PhaseVerdict.NotEvaluable, "took 0.03 s now"),
        ],
        ["about 2.1 years of history."],
        ["lessons"]);

    private const string Expected = """
        Weekly learning digest 2026-W43 (2026-10-19 to 2026-10-25)
        Nothing was applied: the items below are stored records, findings and proposals; deciding or applying any of it is the owner's own act.
        Built at 2026-10-24 10:00 IST.

        1. New and changed cells
        Newest cell-statistics snapshot: as of 2026-10-23.
        1 cell state change(s) recorded this week (changelog).
        - #7 transition ORB|15m|BUY|intraday|FIXED_RR|* [research -> candidate] enough trades
        Nothing was applied in this section: these are stored records, findings and proposals for the owner.

        2. Drift and calibration proposals
        1 open or linked lessons from the drift and calibration monitors.
        - #3 cusum ORB|15m|BUY|intraday|FIXED_RR|* [open] mean R fell
        Note: the newest 200 lessons were read; there may be more, so the counts in this section are undercounts.
        Nothing was applied in this section: these are stored records, findings and proposals for the owner.

        3. Lessons
        Open lessons: 2; linked to a fix: 1 (all monitors; the drift and loss-tag ones are listed in their own sections).
        0 lesson(s) closed or dismissed this week.
        - none
        Note: the newest 200 lessons were read; there may be more, so the counts in this section are undercounts.
        Nothing was applied in this section: these are stored records, findings and proposals for the owner.

        4. Hypotheses and experiments awaiting the owner
        1 hypothesis(es) proposed or judged, waiting for the owner; 1 approved or running.
        - #5 hypothesis [judged] slippage 1 -> 2 (experiment #4): fills dearer
        1 experiment(s) recorded this week.
        - #4 experiment slippage [Better] better in 3 of 3 folds (3 needed); 1 trials in 30 days
        Nothing was applied in this section: these are stored records, findings and proposals for the owner.

        5. Loss-tag trends
        0 open or linked lessons from the growing loss-tag monitor.
        - none
        Note: the newest 200 lessons were read; there may be more, so the counts in this section are undercounts.
        Nothing was applied in this section: these are stored records, findings and proposals for the owner.

        6. Plan outcomes (simulated on plan prices)
        No graded plan for the sessions of this week.
        Plan outcomes are simulated on plan prices, not on fills.
        Nothing was applied in this section: these are stored records, findings and proposals for the owner.

        7. Model registry and the phase check
        1 model(s) as champion or challenger.
        - #2 champion [Eligible] beat both baselines in 3 of 3 folds (3 needed)
        Phase criterion 1: NOT EVALUABLE - beats baselines
          detail: only 3 folds
        Phase criterion 2: MET - calibration
          detail: 1.20 pp
        Phase criterion 3: NOT EVALUABLE - replay time
          detail: the timing is measured on each run; see xd learn phase-check.
        Nothing was applied in this section: these are stored records, findings and proposals for the owner.

        8. Data quality and honest limits
        Everything here rests on about 2.1 years of data and about 3 usable validation folds; fills are modelled, and plan outcomes are simulated on plan prices, not on fills.
        Lists read the newest 200 records of each kind; a note says where there may be more, and then the counts are undercounts.
        Statistics are as stored when each record was made; this digest recomputes nothing and is stored once per ISO week.
        Phase check limit: about 2.1 years of history.
        Nothing was applied in this section: these are stored records, findings and proposals for the owner.

        """;

    [Fact]
    public void The_digest_is_the_hand_checked_text()
    {
        var digest = DigestBuilder.Build(Fixture());

        digest.Text.ShouldBe(Expected.ReplaceLineEndings("\n"));
        digest.Sections.Select(s => s.Key).ShouldBe(["cells", "drift", "lessons", "hypotheses", "loss_tags", "plans", "models", "limits"]);
    }

    [Fact]
    public void The_same_input_gives_the_same_text()
    {
        DigestBuilder.Build(Fixture()).Text.ShouldBe(DigestBuilder.Build(Fixture()).Text);
    }

    [Fact]
    public void Every_section_and_the_text_say_nothing_was_applied()
    {
        var digest = DigestBuilder.Build(Fixture());

        digest.Text.ShouldContain(DigestBuilder.NothingApplied);
        digest.Sections.Select(s => s.Lines[s.Lines.Count - 1]).ShouldAllBe(l => l == DigestBuilder.SectionNote);
        DigestBuilder.NothingApplied.ShouldContain("Nothing was applied");
    }

    [Fact]
    public void An_empty_week_is_an_answer_in_every_section()
    {
        var empty = new DigestInput(Week, Saturday, null, [], [], [], [], 0, 0, [], 0, [], null, [], [], [], []);

        var digest = DigestBuilder.Build(empty);

        digest.Sections.Count.ShouldBe(8);
        digest.Text.ShouldContain("No cell-statistics snapshot is stored yet.");
    }

    [Fact]
    public void A_week_built_after_it_ended_says_the_state_is_the_build_dates()
    {
        var late = DigestBuilder.Build(Fixture() with { BuiltAt = new DateTimeOffset(2026, 10, 27, 9, 30, 0, TimeSpan.FromMinutes(330)) });

        late.Text.ShouldContain("Built at 2026-10-27 09:30 IST. State as of 2026-10-27, not as of the end of the week");
        DigestBuilder.Build(Fixture()).Text.ShouldNotContain("State as of");
    }

    [Fact]
    public void The_built_time_is_shown_in_india_time_whatever_the_offset_it_arrives_in()
    {
        var text = DigestBuilder.Build(Fixture() with { BuiltAt = new DateTimeOffset(2026, 10, 24, 4, 30, 0, TimeSpan.Zero) }).Text;

        text.ShouldContain("Built at 2026-10-24 10:00 IST.");
    }

    [Fact]
    public void The_read_limit_in_the_text_is_the_constant()
    {
        DigestBuilder.Build(Fixture()).Text.ShouldContain($"the newest {DigestBuilder.ReadLimit} lessons");
        DigestBuilder.Build(Fixture()).Text.ShouldContain($"Lists read the newest {DigestBuilder.ReadLimit} records");
    }

    [Fact]
    public void A_graded_week_lists_the_stored_counts_labelled_as_plan_prices()
    {
        var all = new GradeStats("top-all", 4, 2, 1, 1, 1.5, 0.75, 0.5, "indicative only");
        var report = new GradeReportData(TicketGrade.Basis, 3, [new GradeCurve("top-1", [], all with { Name = "top-1" }), new GradeCurve("top-all", [], all)], [all with { Name = "primary" }], [], [], ["a note"]);
        var input = Fixture() with { Plans = report };

        var text = DigestBuilder.Build(input).Text;

        text.ShouldContain("3 graded plan day(s); basis: simulated on plan prices.");
        text.ShouldContain("- all primary and reserve tickets: 4 tickets, 2 triggered, 1 not triggered, 1 not gradable, total 1.50 R (indicative only)");
        text.ShouldContain("- primary: 4 tickets, 2 triggered, 1 not triggered, 1 not gradable, total 1.50 R (indicative only)");
        text.ShouldContain("Note: a note");
    }
}
