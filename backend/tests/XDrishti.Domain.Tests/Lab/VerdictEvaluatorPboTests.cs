using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-03 part B, criterion 16: the PBO gate caps a PASS at MARGINAL in the owner's Gate mode only; a FAIL stays a FAIL; equal to the limit is allowed; an unavailable PBO changes nothing.</summary>
public sealed class VerdictEvaluatorPboTests
{
    private static readonly PboOptions Gate = new(ConfidenceMode.Gate);

    [Fact]
    public void A_pass_with_a_PBO_of_two_thirds_in_gate_mode_becomes_marginal_with_the_reason()
    {
        var cap = VerdictEvaluator.CapByPbo(VerdictOutcome.Pass, Gate, 4.0 / 6.0);

        cap.Outcome.ShouldBe(VerdictOutcome.Marginal);
        cap.Reason.ShouldNotBeNull().ShouldContain("0.6667");
        cap.Reason.ShouldContain("0.5");
        cap.Reason.ShouldContain("Gate mode");
        cap.Reason.ShouldStartWith("capped at MARGINAL");
    }

    [Fact]
    public void A_fail_stays_a_fail_and_a_marginal_stays_marginal_without_a_reason()
    {
        VerdictEvaluator.CapByPbo(VerdictOutcome.Fail, Gate, 0.9).ShouldBe(new PboCap(VerdictOutcome.Fail, null));
        VerdictEvaluator.CapByPbo(VerdictOutcome.Marginal, Gate, 0.9).ShouldBe(new PboCap(VerdictOutcome.Marginal, null));
    }

    [Theory]
    [InlineData(0.49, false)]
    [InlineData(0.5, false)]
    [InlineData(0.5000000001, true)]
    [InlineData(0.51, true)]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    public void The_limit_itself_is_allowed_and_only_a_figure_above_it_caps(double pbo, bool capped) =>
        VerdictEvaluator.CapByPbo(VerdictOutcome.Pass, Gate, pbo).Outcome.ShouldBe(capped ? VerdictOutcome.Marginal : VerdictOutcome.Pass);

    [Fact]
    public void A_configured_limit_is_the_one_compared_with()
    {
        var tight = new PboOptions(ConfidenceMode.Gate, MaxPbo: 0.1);
        var loose = new PboOptions(ConfidenceMode.Gate, MaxPbo: 0.9);

        VerdictEvaluator.CapByPbo(VerdictOutcome.Pass, tight, 0.1).Outcome.ShouldBe(VerdictOutcome.Pass);
        VerdictEvaluator.CapByPbo(VerdictOutcome.Pass, tight, 0.11).Outcome.ShouldBe(VerdictOutcome.Marginal);
        VerdictEvaluator.CapByPbo(VerdictOutcome.Pass, loose, 0.9).Outcome.ShouldBe(VerdictOutcome.Pass);
        VerdictEvaluator.CapByPbo(VerdictOutcome.Pass, loose, 0.91).Outcome.ShouldBe(VerdictOutcome.Marginal);
    }

    [Theory]
    [InlineData(ConfidenceMode.Off)]
    [InlineData(ConfidenceMode.ReportOnly)]
    public void Outside_gate_mode_the_outcome_is_returned_as_it_came_whatever_the_figure(ConfidenceMode mode)
    {
        foreach (var outcome in Enum.GetValues<VerdictOutcome>())
        {
            VerdictEvaluator.CapByPbo(outcome, new PboOptions(mode), 1.0).ShouldBe(new PboCap(outcome, null));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_unavailable_or_unusable_PBO_changes_nothing_even_in_gate_mode(double? pbo) =>
        VerdictEvaluator.CapByPbo(VerdictOutcome.Pass, Gate, pbo).ShouldBe(new PboCap(VerdictOutcome.Pass, null));

    [Fact]
    public void The_gate_only_ever_tightens_for_every_outcome_mode_and_figure()
    {
        foreach (var mode in Enum.GetValues<ConfidenceMode>())
        {
            foreach (var outcome in Enum.GetValues<VerdictOutcome>())
            {
                foreach (double? pbo in new double?[] { null, 0, 0.3, 0.5, 0.51, 1 })
                {
                    var cap = VerdictEvaluator.CapByPbo(outcome, new PboOptions(mode), pbo);

                    ((int)cap.Outcome).ShouldBeGreaterThanOrEqualTo((int)outcome, "VerdictOutcome is ordered Pass, Marginal, Fail: the number never falls");
                    (cap.Outcome != outcome).ShouldBe(cap.Reason is not null);
                }
            }
        }
    }

    [Fact]
    public void Evaluate_itself_is_untouched_by_the_gate()
    {
        var evidence = new OosEvidence(100, 0.40, 1.8, 12, 9, 4, 2, "walk_forward");

        var before = VerdictEvaluator.Evaluate(QualificationGates.Default, evidence, 1, 6);
        VerdictEvaluator.CapByPbo(before.Outcome, Gate, 1.0);
        var after = VerdictEvaluator.Evaluate(QualificationGates.Default, evidence, 1, 6);

        after.Outcome.ShouldBe(before.Outcome);
        after.Gates.Select(g => g.Reason).ShouldBe(before.Gates.Select(g => g.Reason));
        after.Limits.ShouldBe(before.Limits);
        before.Outcome.ShouldBe(VerdictOutcome.Pass);
    }
}
