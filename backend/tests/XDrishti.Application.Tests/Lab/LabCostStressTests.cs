using System.Text.Json;
using XDrishti.Application.Lab;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-05 criteria 3, 5, 8 and 9 at the verdict: the block beside the outcome, report-only changes nothing, Enforce caps at MARGINAL and never raises, an unchecked sheet and a gross run are unavailable and never a pass.</summary>
public sealed class LabCostStressTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly DateOnly D = new(2026, 1, 5);

    private static LabVerdictView Verdict(string outcome = "PASS", double oneXMin = 0.1, bool oneXPassed = true) => new(
        outcome, 1, 1,
        new EvidenceView(60, 0.20, 1.5, 8, 6, 3, 0, "walk_forward"),
        [new GateView("oos_trades", 50, 50, 60, true, "ok"), new GateView("expectancy_R", 0.1, oneXMin, 0.20, oneXPassed, "ok")],
        ["a limit"], [], [], ["a label"]);

    // RExact 0.32 and net 0.20: 1x +0.20, 1.5x +0.14, 2x +0.08 (the spec's passing example)
    private static SelectionTrade[] Good() => [new SelectionTrade("c", D, D, 0.20, 0.32), new SelectionTrade("c", D.AddDays(1), D.AddDays(1), 0.20, 0.32)];

    // RExact 0.22 and net 0.10: 1.5x +0.04, 2x -0.02 (the spec's failing example)
    private static SelectionTrade[] Thin() => [new SelectionTrade("c", D, D, 0.10, 0.22)];

    private static LabVerdictView Apply(CostStressOptions o, LabVerdictView v, SelectionTrade[] trades, bool checkedSheet = true, DelaySignals? delay = null) =>
        new LabCostStress(o).Apply(v, trades, "fixture-a", checkedSheet, delay);

    [Fact]
    public void Off_returns_the_verdict_itself()
    {
        var v = Verdict();

        Apply(new CostStressOptions(ConfidenceMode.Off), v, Good()).ShouldBeSameAs(v);
    }

    [Fact]
    public void The_block_carries_the_three_rows_the_label_the_settings_and_their_hash_and_the_printed_formula()
    {
        var o = new CostStressOptions();

        var block = Apply(o, Verdict(), Good()).CostStress!;

        block.Rows.Select(r => r.Multiplier).ShouldBe([1, 1.5, 2]);
        block.Rows.Select(r => r.Status).ShouldBe(["pass", "pass", "pass"]);
        block.Rows[1].ExpectancyR!.Value.ShouldBe(0.14, 1e-12);
        block.Rows[2].ExpectancyR!.Value.ShouldBe(0.08, 1e-12);
        block.Tradable.ShouldBe("pass");
        block.Settings.ShouldBe(o.KeyText());
        block.SettingsHash.ShouldBe(o.SnapshotHash());
        block.SheetId.ShouldBe("fixture-a");
        block.Formula.ShouldContain("RExact + m x");
        block.Limit.ShouldContain("first-order for slippage");
        block.Delay.ShouldBeNull();
    }

    [Fact]
    public void Report_only_changes_no_outcome_gate_evidence_or_limit_even_when_the_stress_fails()
    {
        var plain = Verdict("PASS");

        var applied = Apply(new CostStressOptions(), plain, Thin());

        applied.CostStress!.Tradable.ShouldBe("fail");
        applied.CostStress.Capped.ShouldBeFalse();
        JsonSerializer.Serialize(applied with { CostStress = null }).ShouldBe(JsonSerializer.Serialize(plain), "byte for byte the verdict it was");
    }

    [Fact]
    public void Enforce_on_caps_a_pass_at_marginal_and_says_which_gates_missed()
    {
        var applied = Apply(new CostStressOptions(Enforce: true), Verdict("PASS"), Thin());

        applied.Outcome.ShouldBe("MARGINAL");
        applied.CostStress!.Capped.ShouldBeTrue();
        applied.Limits.ShouldContain(l => l.StartsWith("capped at MARGINAL by the cost stress gates", StringComparison.Ordinal) && l.Contains("1.5x stress fail", StringComparison.Ordinal) && l.Contains("2x stress fail", StringComparison.Ordinal));
        applied.Gates.ShouldBe(Verdict("PASS").Gates, "the gates are not touched");
    }

    [Theory]
    [InlineData("MARGINAL")]
    [InlineData("FAIL")]
    public void Enforce_on_never_raises_a_lower_outcome(string outcome)
    {
        var applied = Apply(new CostStressOptions(Enforce: true), Verdict(outcome), Good());

        applied.Outcome.ShouldBe(outcome);
        Apply(new CostStressOptions(Enforce: true), Verdict(outcome), Thin()).Outcome.ShouldBe(outcome);
    }

    [Fact]
    public void Enforce_on_with_every_gate_met_leaves_a_pass_a_pass()
    {
        var applied = Apply(new CostStressOptions(Enforce: true), Verdict("PASS"), Good());

        applied.Outcome.ShouldBe("PASS");
        applied.CostStress!.Capped.ShouldBeFalse();
        applied.Limits.ShouldBe(Verdict("PASS").Limits);
    }

    [Fact]
    public void An_unchecked_sheet_makes_the_rows_unavailable_with_the_label_and_under_enforce_caps_a_pass()
    {
        var report = Apply(new CostStressOptions(), Verdict("PASS"), Good(), checkedSheet: false).CostStress!;
        var enforced = Apply(new CostStressOptions(Enforce: true), Verdict("PASS"), Good(), checkedSheet: false);

        report.CostsChecked.ShouldBeFalse();
        report.Rows.ShouldAllBe(r => r.Status == "unavailable" && r.Reason.Contains("costs not checked against a bill", StringComparison.Ordinal));
        report.Tradable.ShouldBe("unavailable");
        enforced.Outcome.ShouldBe("MARGINAL", "unavailable counts as a miss");
    }

    [Fact]
    public void A_gross_run_has_nothing_to_stress_and_reads_unavailable_never_a_number_or_a_pass()
    {
        var gross = new[] { new SelectionTrade("c", D, D, 0.20) };

        var block = Apply(new CostStressOptions(), Verdict(), gross).CostStress!;

        block.Rows.ShouldAllBe(r => r.Status == "unavailable" && r.ExpectancyR == null && r.Reason == LabCostStress.Gross);
        block.Tradable.ShouldBe("unavailable");
    }

    [Fact]
    public void The_one_times_row_follows_the_existing_expectancy_gate_with_its_adjusted_threshold()
    {
        var block = Apply(new CostStressOptions(), Verdict(oneXMin: 0.5, oneXPassed: false), Good()).CostStress!;

        block.Rows[0].Threshold.ShouldBe(0.5);
        block.Rows[0].Status.ShouldBe("fail");
        block.Tradable.ShouldBe("fail");
    }

    private static DelaySignals Signals(double delayedTotal, int filled = 90) =>
        new(1, [.. Enumerable.Range(0, 100).Select(i => (0.20, i < filled ? (double?)(delayedTotal / filled) : null))], null);

    [Fact]
    public void The_delay_test_per_signal_passes_at_eighty_percent_and_fails_at_sixty_five_and_enforce_caps_on_the_miss()
    {
        var ok = Apply(new CostStressOptions(), Verdict(), Good(), delay: Signals(16)).CostStress!.Delay!;
        var bad = Apply(new CostStressOptions(Enforce: true), Verdict("PASS"), Good(), delay: Signals(13));

        ok.Status.ShouldBe("pass");
        ok.Retention!.Value.ShouldBe(0.80, 1e-12);
        ok.Signals.ShouldBe(100);
        ok.DelayedFilled.ShouldBe(90);
        ok.Note.ShouldContain("0 R");
        bad.CostStress!.Delay!.Status.ShouldBe("fail");
        bad.Outcome.ShouldBe("MARGINAL");
        bad.Limits.ShouldContain(l => l.Contains("delay test fail", StringComparison.Ordinal));
    }

    [Fact]
    public void A_delayed_run_that_failed_or_a_base_that_filled_nothing_is_unavailable()
    {
        Apply(new CostStressOptions(), Verdict(), Good(), delay: new DelaySignals(1, [], "boom")).CostStress!.Delay!.Reason.ShouldContain("the delayed run failed: boom");
        Apply(new CostStressOptions(), Verdict(), Good(), delay: new DelaySignals(1, [], null)).CostStress!.Delay!.Status.ShouldBe("unavailable");
    }

    [Fact]
    public void The_block_round_trips_through_the_stored_document_and_an_old_document_reads_without_it()
    {
        var applied = Apply(new CostStressOptions(), Verdict(), Good(), delay: Signals(16));

        var back = JsonSerializer.Deserialize<LabVerdictView>(JsonSerializer.Serialize(applied, Web), Web)!;
        var old = JsonSerializer.Deserialize<LabVerdictView>("""{"outcome":"FAIL","variantCount":1,"adjustmentFactor":1,"evidence":{"oosTrades":0,"folds":0,"positiveFolds":0,"calibrationErrorPp":0,"method":"walk_forward"},"gates":[],"limits":[],"cells":[],"fullPeriod":[],"labels":[]}""", Web)!;

        JsonSerializer.Serialize(back.CostStress, Web).ShouldBe(JsonSerializer.Serialize(applied.CostStress, Web));
        back.CostStress!.Rows.Count.ShouldBe(3);
        old.CostStress.ShouldBeNull();
    }
}
