using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-03: the ranges of <c>Confidence:Deflation</c> and <c>Confidence:Bootstrap</c> (the host refuses to start on a problem).</summary>
public sealed class DeflationOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_and_have_no_problem()
    {
        var d = new DeflationOptions();

        d.Mode.ShouldBe(ConfidenceMode.ReportOnly);
        d.MinProbability.ShouldBe(0.95);
        d.AssumedTrialSrSd.ShouldBeNull();
        d.TBasis.ShouldBe(DeflationBasis.NEff);
        d.Problems().ShouldBeEmpty();
        new BootstrapOptions().Problems().ShouldBeEmpty();
        new BootstrapOptions().Draws.ShouldBeNull();
        new BootstrapOptions().BlockLength.ShouldBe(0);
    }

    [Fact]
    public void Gate_is_not_offered_for_deflation_until_the_owner_decides_after_the_validation()
    {
        new DeflationOptions(ConfidenceMode.Gate).Problems().ShouldHaveSingleItem().ShouldContain("not offered");
        new DeflationOptions(ConfidenceMode.Off).Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.49)]
    [InlineData(1.0)]
    [InlineData(double.NaN)]
    public void A_probability_out_of_range_is_a_problem(double value) => new DeflationOptions(MinProbability: value).Problems().ShouldHaveSingleItem().ShouldContain("MinProbability");

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void An_assumed_spread_out_of_range_is_a_problem(double value) => new DeflationOptions(AssumedTrialSrSd: value).Problems().ShouldHaveSingleItem().ShouldContain("AssumedTrialSrSd");

    [Fact]
    public void The_key_text_records_every_value()
    {
        new DeflationOptions().KeyText().ShouldBe("ReportOnly/0.95/stored/NEff");
        new DeflationOptions(ConfidenceMode.Off, 0.9, 0.04, DeflationBasis.Raw).KeyText().ShouldBe("Off/0.9/0.04/Raw");
        new BootstrapOptions().KeyText(1000).ShouldBe("ReportOnly/1000/auto");
        new BootstrapOptions(ConfidenceMode.Off, 500, 4).KeyText(500).ShouldBe("Off/500/4");
    }

    [Theory]
    [InlineData(99, 0)]
    [InlineData(20001, 0)]
    [InlineData(1000, 1)]
    [InlineData(1000, 61)]
    public void A_bootstrap_value_out_of_range_is_a_problem(int draws, int block) => new BootstrapOptions(Draws: draws, BlockLength: block).Problems().ShouldHaveSingleItem();

    [Fact]
    public void Bootstrap_gate_is_not_a_mode_and_the_edges_are_fine()
    {
        new BootstrapOptions(ConfidenceMode.Gate).Problems().ShouldHaveSingleItem().ShouldContain("Mode");
        new BootstrapOptions(Draws: 100, BlockLength: 2).Problems().ShouldBeEmpty();
        new BootstrapOptions(Draws: 20000, BlockLength: 60).Problems().ShouldBeEmpty();
    }
}
