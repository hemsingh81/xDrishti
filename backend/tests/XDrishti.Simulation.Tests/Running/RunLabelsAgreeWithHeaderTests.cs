using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>Criterion 5 of P3-04b1: the labels of a run (Domain, <see cref="RunLabels"/>) say what the engine's own header says; the header's fixed three now come from <see cref="RunLabels.FixedLabels"/>.</summary>
public sealed class RunLabelsAgreeWithHeaderTests
{
    private static SimulationRun RunWith(FillRules rules) =>
        FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30))], fills: rules.ToCanonicalJson());

    [Fact]
    public void For_the_default_fills_lines_two_to_six_are_the_labels_of_the_header()
    {
        var rules = FillRules.Default;

        RunLabels.Of(RunWith(rules), rules).Lines.Skip(1).Take(5).ShouldBe(SimulationHeader.LabelsOf(rules));
    }

    [Fact]
    public void Without_slippage_only_the_first_differs_and_says_modelled_where_the_header_says_exact()
    {
        var rules = FillRules.Create(0.1, SlippageConfig.None).Value;

        var lines = RunLabels.Of(RunWith(rules), rules).Lines.Skip(1).Take(5).ToList();
        var header = SimulationHeader.LabelsOf(rules);

        header[0].ShouldBe("fills: exact");
        lines[0].ShouldBe("fills: modelled, no slippage, no tick rounding of fills");
        lines.Skip(1).ShouldBe(header.Skip(1));
    }

    [Fact]
    public void The_header_fixed_labels_are_the_ones_of_the_run_labels()
    {
        SimulationHeader.FixedLabels.ShouldBe(RunLabels.FixedLabels);
        SimulationHeader.FixedLabels.ShouldBe(["costs: none", "portfolio limits: not simulated", "sample: in-sample"]);
    }
}
