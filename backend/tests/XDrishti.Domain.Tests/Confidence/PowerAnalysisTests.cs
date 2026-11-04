using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-01 acceptance criteria 6 to 8.</summary>
public sealed class PowerAnalysisTests
{
    [Fact]
    public void Trades_needed_for_a_point_fifteen_edge_at_sigma_one_point_two()
    {
        // ((1.6449 + 0.8416) * 1.2 / 0.15)^2 = 19.892^2 = 395.7
        PowerAnalysis.TradesNeeded(1.2, 0.15, 0.05, 0.80).ShouldBe(396);
    }

    [Theory]
    [InlineData(34, 0.512)]
    [InlineData(11, 0.900)]
    public void The_smallest_visible_edge(double nEff, double expected) => PowerAnalysis.MinDetectableEdge(1.2, nEff, 0.05, 0.80)!.Value.ShouldBe(expected, 1e-3);

    [Fact]
    public void The_sigma_floor_beats_a_small_sample_sd()
    {
        PowerAnalysis.Sigma(0.3, 0.5).ShouldBe(0.5);
        PowerAnalysis.Sigma(1.2, 0.5).ShouldBe(1.2);
        PowerAnalysis.Sigma(double.NaN, 0.5).ShouldBe(0.5);
        PowerAnalysis.Sigma(0, 0.5).ShouldBe(0.5);
    }

    [Theory]
    [InlineData(double.NaN, 0.15, 0.05, 0.8)]
    [InlineData(1.2, 0, 0.05, 0.8)]
    [InlineData(1.2, -0.1, 0.05, 0.8)]
    [InlineData(1.2, double.PositiveInfinity, 0.05, 0.8)]
    [InlineData(0, 0.15, 0.05, 0.8)]
    [InlineData(1.2, 0.15, 0, 0.8)]
    [InlineData(1.2, 0.15, 0.05, 1)]
    public void Unusable_input_gives_null_never_a_number(double sigma, double delta, double alpha, double power) => PowerAnalysis.TradesNeeded(sigma, delta, alpha, power).ShouldBeNull();

    [Fact]
    public void An_unusable_effective_n_gives_no_edge()
    {
        PowerAnalysis.MinDetectableEdge(1.2, 0, 0.05, 0.8).ShouldBeNull();
        PowerAnalysis.MinDetectableEdge(1.2, double.NaN, 0.05, 0.8).ShouldBeNull();
    }

    [Fact]
    public void Years_to_evidence_for_eleven_effective_trades_over_two_point_one_years()
    {
        var years = PowerAnalysis.YearsToEvidence(11, 2.1, 396)!.Value;

        years.ShouldBe(75.6, 0.1);
        PowerAnalysis.YearsText(years).ShouldBe("about 76 years at this rate (if the rate and the edge hold)");
    }

    [Fact]
    public void A_zero_rate_is_unavailable()
    {
        PowerAnalysis.YearsToEvidence(0, 2.1, 396).ShouldBeNull();
        PowerAnalysis.YearsToEvidence(11, 0, 396).ShouldBeNull();
        PowerAnalysis.YearsText(null).ShouldStartWith("unavailable");
    }
}
