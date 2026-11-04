using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-01 acceptance criterion 10 (and the stored-verdict regression of review fix 8): effective n beside the verdict in ReportOnly changes nothing, and in Gate changes only the trades gate.</summary>
public sealed class VerdictEffectiveNTests
{
    private static readonly QualificationGates Gates = QualificationGates.Default;   // 30 trades; 16 variants -> factor 2.0 -> threshold 60

    private static DependenceAssessment Dep(ConfidenceMode mode, double? nEff, int n = 34) =>
        new(mode, n, nEff, nEff.HasValue ? n / nEff : null, nEff.HasValue ? "cluster" : "none", 396, 0.9, 273, 800, 2.1, 75.6, nEff.HasValue ? Math.Ceiling(nEff.Value) >= 60 : null, false, nEff.HasValue ? string.Empty : "unavailable: x");

    private static OosEvidence Evidence(DependenceAssessment? dep) => new(34, 0.9, 2.5, 12, 10, 3, 2, "walk_forward", dep);

    private static Verdict Judge(DependenceAssessment? dep) => VerdictEvaluator.Evaluate(Gates, Evidence(dep), 16, 6);

    [Fact]
    public void Report_only_equals_the_verdict_without_the_dependence_and_adds_a_note_not_a_limit()
    {
        var plain = Judge(null);
        var report = Judge(Dep(ConfidenceMode.ReportOnly, 11));

        report.Outcome.ShouldBe(plain.Outcome);
        report.Gates.ShouldBe(plain.Gates);
        report.Limits.ShouldBe(plain.Limits);
        report.Gates[0].Actual.ShouldBe(34);
        report.Gates[0].Passed.ShouldBeFalse();
        report.Notes!.Single().ShouldBe("34 trades, effective 11 of 60 needed; not used in the verdict: it would read FAIL on trades if effective n were used");
        plain.Notes.ShouldBeNull();
    }

    [Fact]
    public void Off_changes_nothing_and_says_nothing()
    {
        var v = Judge(Dep(ConfidenceMode.Off, 11));

        v.Gates.ShouldBe(Judge(null).Gates);
        v.Notes.ShouldBeNull();
    }

    [Fact]
    public void Gate_reads_eleven_against_sixty_a_hard_miss_and_changes_only_the_trades_gate()
    {
        var plain = Judge(null);
        var gate = Judge(Dep(ConfidenceMode.Gate, 11));

        gate.Outcome.ShouldBe(VerdictOutcome.Fail);
        plain.Outcome.ShouldBe(VerdictOutcome.Marginal);   // 34 of 60 is a miss but not below half
        gate.Gates[0].Actual.ShouldBe(11);
        gate.Gates[0].Adjusted.ShouldBe(60);
        gate.Gates[0].Passed.ShouldBeFalse();
        gate.Gates[0].Reason.ShouldContain("effective n used instead of 34 trades");
        gate.Gates.Skip(1).ShouldBe(plain.Gates.Skip(1));
        gate.Notes!.Single().ShouldContain("used in the verdict (Gate mode)");
    }

    [Fact]
    public void Gate_passes_when_the_rounded_up_effective_n_reaches_the_threshold()
    {
        var gate = VerdictEvaluator.Evaluate(Gates, new OosEvidence(100, 0.9, 2.5, 12, 10, 3, 2, "walk_forward", Dep(ConfidenceMode.Gate, 59.2, 100)), 16, 6);

        gate.Gates[0].Actual.ShouldBe(60);
        gate.Gates[0].Passed.ShouldBeTrue();
    }

    [Fact]
    public void Gate_with_effective_n_unavailable_never_passes_and_says_so()
    {
        var evidence = new OosEvidence(100, 0.9, 2.5, 12, 10, 3, 2, "walk_forward", Dep(ConfidenceMode.Gate, null, 100));

        var gate = VerdictEvaluator.Evaluate(Gates, evidence, 16, 6);

        gate.Gates[0].Passed.ShouldBeFalse();
        gate.Gates[0].Actual.ShouldBe(100);
        gate.Gates[0].Reason.ShouldContain("effective n unavailable");
        gate.Outcome.ShouldBe(VerdictOutcome.Marginal);   // missed, but not hard: the plain count is 100 of 60
        gate.Notes!.Single().ShouldContain("effective n unavailable");
    }

    [Fact]
    public void Report_only_with_effective_n_unavailable_says_so_and_changes_nothing()
    {
        var evidence = new OosEvidence(100, 0.9, 2.5, 12, 10, 3, 2, "walk_forward", Dep(ConfidenceMode.ReportOnly, null, 100));

        var v = VerdictEvaluator.Evaluate(Gates, evidence, 16, 6);

        v.Gates[0].Passed.ShouldBeTrue();
        v.Outcome.ShouldBe(VerdictOutcome.Pass);
        v.Notes!.Single().ShouldStartWith("effective n unavailable");
    }

    [Theory]
    [InlineData(45.0, false)]   // 16 variants: the adjusted minimum is 60, so 45 is below it although above the configured 30
    [InlineData(59.2, true)]
    public void Enough_by_effective_n_is_judged_against_the_adjusted_minimum_where_the_factor_is_known(double nEff, bool enough)
    {
        var evidence = new OosEvidence(100, 0.9, 2.5, 12, 10, 3, 2, "walk_forward", Dep(ConfidenceMode.ReportOnly, nEff, 100) with { EnoughByNEff = true });

        var v = VerdictEvaluator.Evaluate(Gates, evidence, 16, 6);

        v.Dependence!.EnoughByNEff.ShouldBe(enough);
    }

    [Fact]
    public void An_unavailable_note_does_not_read_unavailable_twice()
    {
        var evidence = new OosEvidence(100, 0.9, 2.5, 12, 10, 3, 2, "walk_forward", Dep(ConfidenceMode.ReportOnly, null, 100));

        VerdictEvaluator.Evaluate(Gates, evidence, 16, 6).Notes!.Single().ShouldBe("effective n unavailable (x); 100 trades shown alone");
    }

    [Fact]
    public void A_pass_stays_a_pass_in_report_only_even_when_effective_n_is_below_the_gate()
    {
        var evidence = new OosEvidence(100, 0.9, 2.5, 12, 10, 3, 2, "walk_forward", Dep(ConfidenceMode.ReportOnly, 11, 100));

        var v = VerdictEvaluator.Evaluate(Gates, evidence, 16, 6);

        v.Outcome.ShouldBe(VerdictOutcome.Pass);
        v.Limits.ShouldBeEmpty();
        v.Notes!.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(ConfidenceMode.Off)]
    [InlineData(ConfidenceMode.ReportOnly)]
    public void A_stored_evidence_without_the_block_is_judged_identically_in_off_and_report_only(ConfidenceMode mode)
    {
        // the numbers of a stored P5-04 document (no dependence member): outcome, six gates and reasons are what they were
        var stored = new OosEvidence(88, 0.31, 1.45, 8, 6, 5.5, 0, "walk_forward");

        var before = VerdictEvaluator.Evaluate(Gates, stored, 4, 6);
        var after = VerdictEvaluator.Evaluate(Gates, stored with { Dependence = Dep(mode, 20, 88) }, 4, 6);

        after.Outcome.ShouldBe(before.Outcome);
        after.Gates.ShouldBe(before.Gates);
        after.Limits.ShouldBe(before.Limits);
    }
}
