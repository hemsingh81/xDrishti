using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>
/// CF-03 acceptance criterion 6 (with the power arm of review fix 1): 400 seeded families of 16 variants of 100 trades; the best variant, with the spread of the 16 sample Sharpe ratios as <c>sigma_SR</c>, is judged by the deflated Sharpe ratio. The null arm (every variant a pure-noise
/// iid result with mean 0 and sd 1 R) must pass <c>DSR &gt;= 0.95</c> at most 10 % of the time; the power arm (one variant with a true per-trade Sharpe ratio of 0.3 among 15 null ones) must pass at least 50 %. Gate is offered only if both hold. The measured values are in ADR 0205.
/// </summary>
public sealed class DeflatedSharpeNullSimulationTests
{
    private const int Families = 400;
    private const int Variants = 16;
    private const int Trades = 100;

    private static (double Dsr, double BestMean) Family(SeededRandom rng, double signal)
    {
        var moments = new List<TradeMoments>(Variants);
        for (var v = 0; v < Variants; v++)
        {
            var r = new double[Trades];
            for (var i = 0; i < Trades; i++)
            {
                r[i] = Normal(rng) + (v == 0 ? signal : 0);
            }

            moments.Add(TradeMoments.Of(r));
        }

        var srs = moments.Select(m => m.Sr).ToList();
        var mean = srs.Average();
        var sigma = Math.Sqrt(srs.Sum(s => (s - mean) * (s - mean)) / (Variants - 1));
        var best = moments.MaxBy(m => m.Sr)!;
        return (DeflatedSharpe.Deflated(best.Sr, Variants, sigma, Trades, best.Skew, best.Kurt).Value!.Value, best.Mean);
    }

    private static double Normal(SeededRandom rng) => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

    private static (double DsrShare, double ExpectancyGateShare) Share(ulong seed, double signal)
    {
        var rng = new SeededRandom(seed);
        var results = Enumerable.Range(0, Families).Select(_ => Family(rng, signal)).ToList();
        var bar = QualificationGates.Default.MinExpectancyR * MultipleTesting.Factor(Variants);
        return (results.Count(r => r.Dsr >= 0.95) / (double)Families, results.Count(r => r.BestMean >= bar) / (double)Families);
    }

    [Fact]
    public void The_null_arm_passes_the_bar_in_at_most_ten_percent_of_families_and_so_does_the_heuristic_expectancy_gate()
    {
        var (dsr, heuristic) = Share(20261104, 0);

        dsr.ShouldBeLessThanOrEqualTo(0.10);
        heuristic.ShouldBeLessThanOrEqualTo(0.10);
    }

    [Fact]
    public void The_power_arm_measures_in_its_band_and_sees_the_edge_more_often_than_the_null()
    {
        // measured 10.5 %; the analytic ceiling with the spread known is about a third, so the 50 % arm of the spec was out of reach at 100 trades and 16 trials
        var (dsr, _) = Share(20261105, 0.3);
        var (nullShare, _) = Share(20261104, 0);

        dsr.ShouldBeInRange(0.05, 0.20);
        dsr.ShouldBeGreaterThan(nullShare, "the figure does see the edge, only not at 95 %");
    }

    [Fact]
    public void Gate_is_refused_because_the_spec_requires_both_arms_and_the_power_arm_is_under_half()
    {
        new DeflationOptions(ConfidenceMode.Gate).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_simulation_is_deterministic()
    {
        Share(7, 0).ShouldBe(Share(7, 0));
    }
}
