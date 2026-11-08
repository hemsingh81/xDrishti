using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-03 part B: the ranges of <c>Confidence:Pbo</c> (the host refuses to start on a problem) and the text kept with the figure.</summary>
public sealed class PboOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_report_only_and_have_no_problem()
    {
        var o = new PboOptions();

        o.Mode.ShouldBe(ConfidenceMode.ReportOnly);
        o.Blocks.ShouldBe(8);
        o.MinVariants.ShouldBe(10);
        o.MaxPbo.ShouldBe(0.5);
        o.Metric.ShouldBe(PboMetric.Mean);
        o.IsOn.ShouldBeTrue();
        o.IsGate.ShouldBeFalse();
        o.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Gate_is_a_mode_the_owner_may_set_and_off_is_not_on()
    {
        new PboOptions(ConfidenceMode.Gate).Problems().ShouldBeEmpty();
        new PboOptions(ConfidenceMode.Gate).IsGate.ShouldBeTrue();
        new PboOptions(ConfidenceMode.Off).IsOn.ShouldBeFalse();
        new PboOptions((ConfidenceMode)7).Problems().ShouldHaveSingleItem().ShouldContain("Pbo:Mode");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(18)]
    [InlineData(0)]
    [InlineData(-4)]
    public void Blocks_outside_four_to_sixteen_or_odd_are_a_problem(int blocks) => new PboOptions(Blocks: blocks).Problems().ShouldHaveSingleItem().ShouldContain("Blocks");

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(16)]
    public void Even_blocks_from_four_to_sixteen_are_fine(int blocks) => new PboOptions(Blocks: blocks).Problems().ShouldBeEmpty();

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public void Minimum_variants_edges(int value, bool problem) => new PboOptions(MinVariants: value).Problems().Count.ShouldBe(problem ? 1 : 0);

    [Theory]
    [InlineData(0.0999, true)]
    [InlineData(0.1, false)]
    [InlineData(0.9, false)]
    [InlineData(0.9001, true)]
    [InlineData(double.NaN, true)]
    [InlineData(double.PositiveInfinity, true)]
    public void The_limit_edges(double value, bool problem) => new PboOptions(MaxPbo: value).Problems().Count.ShouldBe(problem ? 1 : 0);

    [Fact]
    public void An_undefined_metric_is_a_problem()
    {
        new PboOptions(Metric: PboMetric.Sharpe).Problems().ShouldBeEmpty();
        new PboOptions(Metric: (PboMetric)9).Problems().ShouldHaveSingleItem().ShouldContain("Metric");
    }

    [Fact]
    public void Every_problem_is_listed()
    {
        new PboOptions((ConfidenceMode)9, 3, 1, 2.0, (PboMetric)9).Problems().Count.ShouldBe(5);
    }

    [Fact]
    public void The_key_text_records_every_value()
    {
        new PboOptions().KeyText().ShouldBe("ReportOnly/8/10/0.5/Mean");
        new PboOptions(ConfidenceMode.Gate, 12, 6, 0.35, PboMetric.Sharpe).KeyText().ShouldBe("Gate/12/6/0.35/Sharpe");
    }
}
