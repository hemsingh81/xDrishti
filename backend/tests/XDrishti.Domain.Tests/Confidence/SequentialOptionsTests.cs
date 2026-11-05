using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-09 (ADR 0213): the settings of the sequential failure test. The spec's numbers, every number in its range, the hash changes with every setting, and the honest statements are pinned.</summary>
public sealed class SequentialOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_numbers_and_usable()
    {
        var o = new SequentialOptions();

        (o.Mu1, o.Sigma, o.Alpha, o.BetaCaution, o.BetaFail, o.SigmaFromN).ShouldBe((0.2, 1.5, 0.05, 0.20, 0.05, 20));
        o.Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 1.5, 0.05, 0.2, 0.05, 20)]
    [InlineData(0.2, 0.4, 0.05, 0.2, 0.05, 20)]
    [InlineData(0.2, 1.5, 0, 0.2, 0.05, 20)]
    [InlineData(0.2, 1.5, 0.05, 0.6, 0.05, 20)]
    [InlineData(0.2, 1.5, 0.5, 0.5, 0.05, 20)]
    [InlineData(0.2, 1.5, 0.05, 0.2, 0.2, 20)]
    [InlineData(0.2, 1.5, 0.05, 0.2, 0.05, 4)]
    [InlineData(double.NaN, 1.5, 0.05, 0.2, 0.05, 20)]
    public void A_value_out_of_range_is_a_problem(double mu1, double sigma, double alpha, double betaCaution, double betaFail, int sigmaFromN)
    {
        new SequentialOptions(mu1, sigma, alpha, betaCaution, betaFail, sigmaFromN).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_hash_changes_with_every_setting_and_not_otherwise()
    {
        var baseline = new SequentialOptions().SnapshotHash();

        new SequentialOptions().SnapshotHash().ShouldBe(baseline);
        baseline.Length.ShouldBe(64);
        new[]
        {
            new SequentialOptions(Mu1: 0.25), new SequentialOptions(Sigma: 1.6), new SequentialOptions(Alpha: 0.04), new SequentialOptions(BetaCaution: 0.25), new SequentialOptions(BetaFail: 0.04), new SequentialOptions(SigmaFromN: 30),
        }.Select(o => o.SnapshotHash()).ShouldAllBe(h => h != baseline);
    }

    [Fact]
    public void The_statements_name_the_model_the_odds_the_indifference_point_the_rho_and_the_fixed_gates_and_the_cumulative_bound()
    {
        var text = string.Join('\n', SequentialWording.Statements(new SequentialOptions(), 0.3, rhoIsPlaceholder: true));

        text.ShouldContain("Gaussian");
        text.ShouldContain("4.75 to 1");
        text.ShouldContain("19 to 1");
        text.ShouldContain("at most 5.3 %");
        text.ShouldContain("about 0.33 % per later return to the cap");
        text.ShouldContain("mu1 / 2 = 0.1 R");
        text.ShouldContain("rho 0.30 (a placeholder until CF-01 estimates it)");
        text.ShouldContain("never a pass");
        text.ShouldContain("fixed gates of P10 still decide a pass");
        string.Join('\n', SequentialWording.Statements(new SequentialOptions(), 0.25, rhoIsPlaceholder: false)).ShouldContain("rho 0.25 (estimated)");
    }
}
