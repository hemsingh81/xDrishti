using XDrishti.Api.Simulate.Assist;
using XDrishti.Api.Simulate.Gate;
using XDrishti.SimReader;
using static XDrishti.Api.IntegrationTests.Simulate.AssistSupport;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09d acceptance 3: every status of the pure rules and the precedence between them. A verdict never hides a category and the counts are reported independently.</summary>
public sealed class GateRulesTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Engine = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    private static AssistRow Row(int seq = 1, string? unavailable = null, IReadOnlyList<MinuteComparison>? bars = null) =>
        new(new PackedTrade(seq, "ORB|NSE:S" + seq + "|Buy|2026-03-02", "NSE:S" + seq, ReaderSide.Buy, Date, "ORB", 15, 2m), unavailable, "auto:fake", Read(BuyDay()), 0.05m, bars ?? [new MinuteComparison("09:15", "match", true)], StoredReading: Read(BuyDay()));

    private static RowVerdict Agree(int seq = 1) => new(Row(seq), Verdicts.AgreeAll, [], []);

    private static RowVerdict Defect(int seq = 1) => new(Row(seq), Verdicts.ReaderDiffers, ["exit reason"], []);

    private static RowVerdict Data(int seq = 1) => new(Row(seq, bars: [new MinuteComparison("09:15", "match (1 tick)", true, 1, 0.05m)]), Verdicts.DataSensitive, [], ["stop"]);

    private static RowVerdict Gross(int seq = 1) => new(Row(seq, bars: [new MinuteComparison("09:46", "differs by 50 ticks", false, 50, 2.5m, true)]), Verdicts.BarsDiffer, [], []);

    private static RowVerdict Unavailable(int seq = 1) => new(Row(seq, "HTTP 401"), Verdicts.SourceUnavailable, [], []);

    private static SpotResult Spot(SpotOutcome outcome) => new(new SpotLine(9, "spot|flagged_day||NSE:S1|2026-03-02", "flagged_day", string.Empty, "NSE:S1", Date), outcome, "checked", "not checked");

    private static GateDecision Decide(IReadOnlyList<RowVerdict> rows, params SpotResult[] spots) => GateRules.Decide(rows, spots, Engine);

    [Fact]
    public void All_clear_is_PASS_and_exits_zero()
    {
        var d = Decide([Agree(1), Agree(2)], Spot(SpotOutcome.Confirmed));

        d.Status.ShouldBe(GateStatus.Pass);
        GateStatuses.ExitCode(d.Status).ShouldBe(0);
        d.Counts.ShouldBe(new GateCounts(2, 0, 0, 0, 0, 1, 0, 0));
        d.NextActions.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_data_sensitive_row_is_PASS_WITH_DATA_FINDINGS_with_its_fields_and_gap_listed()
    {
        var d = Decide([Agree(1), Data(2)]);

        d.Status.ShouldBe(GateStatus.PassWithDataFindings);
        GateStatuses.ExitCode(d.Status).ShouldBe(0);
        d.DataFindings.Single().Detail.ShouldContain("stop");
        d.DataFindings.Single().Detail.ShouldContain("paired minutes");
        d.NextActions[0].ShouldContain("not P3 blockers");
    }

    [Fact]
    public void A_source_unavailable_row_is_RERUN_NEEDED()
    {
        var d = Decide([Agree(1), Unavailable(2)]);

        d.Status.ShouldBe(GateStatus.RerunNeeded);
        GateStatuses.ExitCode(d.Status).ShouldBe(10);
        d.Reruns.Single().Detail.ShouldBe("HTTP 401");
        d.NextActions[0].ShouldContain("Reconnect");
    }

    [Fact]
    public void A_gross_bar_problem_is_DATA_REPAIR_NEEDED_and_names_instrument_and_day()
    {
        var d = Decide([Gross(1)]);

        d.Status.ShouldBe(GateStatus.DataRepairNeeded);
        GateStatuses.ExitCode(d.Status).ShouldBe(11);
        d.Repairs.Single().Symbol.ShouldBe("NSE:S1");
        d.NextActions[0].ShouldContain("NSE:S1 2026-03-02");
        d.NextActions[0].ShouldContain("backfill");
    }

    [Fact]
    public void A_reader_difference_on_the_stored_bars_is_BLOCKED_DEFECT_with_a_repro()
    {
        var d = Decide([Defect(1)]);

        d.Status.ShouldBe(GateStatus.BlockedDefect);
        GateStatuses.ExitCode(d.Status).ShouldBe(12);
        var repro = d.Defects.Single().Detail;
        repro.ShouldContain("ORB or_min=15 rr=2 Buy NSE:S1 2026-03-02");
        repro.ShouldContain("the engine row is absent");
    }

    [Fact]
    public void The_repro_of_a_defect_holds_both_sides_values()
    {
        var engine = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["ORB|NSE:S1|Buy|2026-03-02"] = new Dictionary<string, string> { ["exit_reason"] = "stop", ["entry_price"] = "102.5" } };

        var repro = GateRules.Decide([Defect(1)], [], engine).Defects.Single().Detail;

        repro.ShouldContain("differs on exit reason");
        repro.ShouldContain("reader: entry 2026-03-02 09:45 @ 102.5");
        repro.ShouldContain("engine: entry  @ 102.5");
        repro.ShouldContain("stop");
    }

    [Fact]
    public void Cannot_read_and_an_empty_pack_and_a_contradicted_spot_are_each_BLOCKED_DEFECT()
    {
        Decide([new RowVerdict(Row(), Verdicts.CannotRead, [])]).Status.ShouldBe(GateStatus.BlockedDefect);
        Decide([]).Status.ShouldBe(GateStatus.BlockedDefect);
        var d = Decide([Agree()], Spot(SpotOutcome.Contradicted));
        d.Status.ShouldBe(GateStatus.BlockedDefect);
        d.Counts.SpotsContradicted.ShouldBe(1);
    }

    [Fact]
    public void Not_machine_checkable_spots_are_counted_and_never_change_the_status()
    {
        var d = Decide([Agree()], Spot(SpotOutcome.NotMachineCheckable));

        d.Status.ShouldBe(GateStatus.Pass);
        d.Counts.SpotsNotCheckable.ShouldBe(1);
        d.NextActions.ShouldContain(a => a.Contains("not machine-checkable", StringComparison.Ordinal));
    }

    [Fact]
    public void Blocked_defect_beats_everything_and_no_count_is_masked()
    {
        var d = Decide([Defect(1), Gross(2), Unavailable(3), Data(4), Agree(5)]);

        d.Status.ShouldBe(GateStatus.BlockedDefect);
        d.Counts.ShouldBe(new GateCounts(5, 1, 1, 1, 1, 0, 0, 0));
        d.NextActions[^1].ShouldContain("1 defects, 1 data repairs, 1 rerun rows, 1 data findings");
    }

    [Fact]
    public void A_row_with_a_defect_and_a_gross_gap_is_counted_in_both()
    {
        var row = new RowVerdict(Row(bars: [new MinuteComparison("09:46", "differs by 50 ticks", false, 50, 2.5m, true)]), Verdicts.ReaderDiffers, ["entry price"], ["stop"]);

        var d = Decide([row]);

        d.Status.ShouldBe(GateStatus.BlockedDefect);
        d.Counts.Defects.ShouldBe(1);
        d.Counts.Repairs.ShouldBe(1);
        d.Counts.DataFindings.ShouldBe(0);
    }

    [Fact]
    public void The_precedence_below_blocked_is_repair_then_rerun_then_data_findings()
    {
        Decide([Gross(1), Unavailable(2), Data(3)]).Status.ShouldBe(GateStatus.DataRepairNeeded);
        Decide([Unavailable(2), Data(3)]).Status.ShouldBe(GateStatus.RerunNeeded);
        Decide([Data(3), Agree(4)]).Status.ShouldBe(GateStatus.PassWithDataFindings);
    }

    [Fact]
    public void The_status_codes_are_stable_words()
    {
        string[] codes = [.. Enum.GetValues<GateStatus>().Select(GateStatuses.Code)];

        codes.ShouldBe(["PASS", "PASS_WITH_DATA_FINDINGS", "RERUN_NEEDED", "DATA_REPAIR_NEEDED", "BLOCKED_DEFECT"]);
    }

    private static RowVerdict Hole(string words = "missing in stored", string minute = "09:50") =>
        new(Row(bars: [new MinuteComparison(minute, words, false, 0, null, true)]), Verdicts.BarsDiffer, [], []);

    [Fact]
    public void A_hole_in_the_data_alone_is_a_data_finding_with_the_side_and_the_between_flag_not_a_repair()
    {
        var inside = Decide([Hole("missing in stored", "09:50")]);
        var outside = Decide([Hole("missing in second source", "09:20")]);

        inside.Status.ShouldBe(GateStatus.PassWithDataFindings);
        inside.Counts.Repairs.ShouldBe(0);
        inside.DataFindings.Single().Detail.ShouldContain("09:50 missing in stored (between the entry and exit minutes: YES)");
        outside.DataFindings.Single().Detail.ShouldContain("09:20 missing in second source (between the entry and exit minutes: no)");
    }

    [Fact]
    public void A_hole_with_a_reader_difference_is_BLOCKED_DEFECT_and_the_hole_is_still_counted()
    {
        var row = new RowVerdict(Row(bars: [new MinuteComparison("09:50", "missing in stored", false, 0, null, true)]), Verdicts.ReaderDiffers, ["exit reason"], []);

        var d = Decide([row]);

        d.Status.ShouldBe(GateStatus.BlockedDefect);
        d.Counts.DataFindings.ShouldBe(1);
        d.Counts.Repairs.ShouldBe(0);
    }

    [Fact]
    public void A_hole_and_a_gross_gap_is_still_DATA_REPAIR_NEEDED_and_the_actions_say_the_backfill_has_a_lookback()
    {
        var d = Decide([Hole(), Gross(2)]);

        d.Status.ShouldBe(GateStatus.DataRepairNeeded);
        d.NextActions[0].ShouldContain("cannot reach older days");
        d.Counts.DataFindings.ShouldBe(1);
    }

    [Fact]
    public void The_pass_with_findings_action_says_old_holes_stay_open()
    {
        Decide([Hole()]).NextActions[0].ShouldContain("older than the lookback cannot be reached");
    }
}
