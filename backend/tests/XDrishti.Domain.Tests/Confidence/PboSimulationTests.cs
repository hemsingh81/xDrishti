using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>
/// CF-03 part B power check (ADR 0228), the counterpart of the deflated Sharpe null simulation. 200 seeded families of 17 variants over 520 sessions, 8 blocks. The noise arm (every variant mean 0, sd 1 R) shows how often PBO is above the 0.5 limit when there is nothing to find
/// (the gate catches such a PASS); the edge arm (one variant with a true mean of 0.15 R a day among 16 noise ones) shows how often a real edge is capped by mistake. Gate is offered only if the edge arm is capped in at most 10 % of families. A weak-edge arm (0.05 R a day) pins how little PBO protects a weak edge.
/// </summary>
public sealed class PboSimulationTests
{
    private const int Families = 200;
    private const int Variants = 17;
    private const int Days = 520;

    private static double Normal(SeededRandom rng) => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

    private static double AboveLimitShare(ulong seed, double signal)
    {
        var rng = new SeededRandom(seed);
        var above = 0;
        for (var f = 0; f < Families; f++)
        {
            var series = new List<double[]>(Variants);
            for (var v = 0; v < Variants; v++)
            {
                var s = new double[Days];
                for (var d = 0; d < Days; d++)
                {
                    s[d] = Normal(rng) + (v == 0 ? signal : 0);
                }

                series.Add(s);
            }

            var pbo = Cscv.Pbo(series, 8).Pbo!.Value;
            if (pbo > 0.5)
            {
                above++;
            }
        }

        return above / (double)Families;
    }

    private static readonly Lazy<double> Strong = new(() => AboveLimitShare(20261108, 0.15));
    private static readonly Lazy<double> Weak = new(() => AboveLimitShare(20261110, 0.05));
    private static readonly Lazy<double> Noise = new(() => AboveLimitShare(20261109, 0));

    [Fact]
    public void A_real_edge_is_capped_by_mistake_in_at_most_ten_percent_of_families()
    {
        // observed 0.055 (11 of 200); the seeds are fixed, so the margin only covers a change of the random stream
        Strong.Value.ShouldBeLessThanOrEqualTo(0.10);
        Strong.Value.ShouldBe(0.055, 0.03);
    }

    [Fact]
    public void A_weaker_edge_of_a_twentieth_of_an_r_is_capped_in_about_the_noise_rate_so_the_gate_does_not_protect_it()
    {
        // observed 0.455 (91 of 200). The pinned figure is the honest limit of the gate: an edge this weak among 16 noise variants is hardly told from noise by PBO
        Weak.Value.ShouldBe(0.455, 0.05);
    }

    [Fact]
    public void Pure_noise_is_above_the_limit_far_more_often_than_a_real_edge_so_the_figure_has_power()
    {
        // observed 0.545 (109 of 200); the binomial sd of 200 families near 0.5 is 0.035, so 0.05 is about 1.4 sd of sampling and the seeds are fixed anyway
        Noise.Value.ShouldBe(0.545, 0.05);
        Noise.Value.ShouldBeGreaterThan(Strong.Value + 0.2);
    }

    [Fact]
    public void The_simulation_is_deterministic() => AboveLimitShare(7, 0.15).ShouldBe(AboveLimitShare(7, 0.15));
}
