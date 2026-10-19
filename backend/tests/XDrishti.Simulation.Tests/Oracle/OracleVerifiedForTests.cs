using System.Reflection;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Annex C of P3-08 (criterion C5): the engine label is true only while the engine is at the version the oracle comparison was shown for. A new engine version fails this class until the comparison is rerun and the constant edited on purpose.
/// </summary>
public sealed class OracleVerifiedForTests
{
    [Fact]
    public void The_label_is_true_only_while_the_engine_version_is_the_one_the_comparison_was_shown_for()
    {
        OracleVerification.LabelIsBound(RunLabels.EngineVerified, OracleVerification.OracleVerifiedFor, SimulationEngine.Version)
            .ShouldBeTrue($"RunLabels.EngineVerified is {RunLabels.EngineVerified} but the oracle comparison was shown for '{OracleVerification.OracleVerifiedFor}' and the engine is '{SimulationEngine.Version}': rerun the comparison, then edit OracleVerifiedFor on purpose");
    }

    [Fact]
    public void The_engine_label_names_the_version_the_comparison_was_shown_for()
    {
        RunLabels.EngineVerification.ShouldContain(OracleVerification.OracleVerifiedFor, Case.Sensitive);
        RunLabels.EngineVerification.ShouldContain(SimulationEngine.Version, Case.Sensitive);
    }

    [Theory]
    [InlineData(true, "sim-5", "sim-5", true)]
    [InlineData(true, "sim-5", "sim-6", false)]
    [InlineData(true, "sim-5", "", false)]
    [InlineData(false, "sim-5", "sim-6", true)]
    [InlineData(false, "sim-5", "sim-5", true)]
    public void The_sentinel_a_changed_engine_version_with_the_label_still_true_is_rejected(bool verified, string verifiedFor, string engine, bool bound)
    {
        OracleVerification.LabelIsBound(verified, verifiedFor, engine).ShouldBe(bound);
    }

    [Fact]
    public void The_constant_is_committed_text_and_not_read_from_a_file_the_suite_writes()
    {
        var field = typeof(OracleVerification).GetField(nameof(OracleVerification.OracleVerifiedFor), BindingFlags.NonPublic | BindingFlags.Static);

        field.ShouldNotBeNull();
        field.IsLiteral.ShouldBeTrue("a compile-time constant: nothing the suite does at run time can change it");
        field.GetRawConstantValue().ShouldBe("sim-5");
    }

    [Fact]
    public void The_comparison_the_label_stands_on_has_no_disagreement_in_any_run_of_the_walker_the_gate_or_the_costing()
    {
        Corpus.All.Sum(t => t.DisagreementCount).ShouldBe(0, "a disagreement between the oracle and the engine in the walker or the gate");
        CostCorpus.All.Sum(t => t.DisagreementCount).ShouldBe(0, "a disagreement between the oracle and the engine in the costing");
    }
}
