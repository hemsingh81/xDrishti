using XDrishti.Domain.Lab;
using XDrishti.Domain.Pairs;

namespace XDrishti.Domain.Tests.Pairs;

/// <summary>Spec P14-12 criteria 2 to 5 and 7: seeded property tests with fixed thresholds.</summary>
public sealed class PairsPropertyTests
{
    [Fact]
    public void An_exact_linear_relation_gives_beta_and_no_test_statistic()
    {
        var rng = new SeededRandom(11);
        for (var i = 0; i < 200; i++)
        {
            var x = PairsData.Walk(rng, 120);
            var a = (PairsData.Uniform(rng) * 4) - 2;
            var b = (PairsData.Uniform(rng) < 0.5 ? -1 : 1) * (0.2 + (PairsData.Uniform(rng) * 2.8));
            var y = x.Select(v => a + (b * v)).ToArray();

            var fit = HedgeRatio.Ols(y, x).Value;
            var test = EngleGranger.Test(y, x);

            fit.Beta.ShouldBe(b, 1e-10);
            test.IsAvailable.ShouldBeFalse();
            test.Reason.ShouldBe(PairsReason.ZeroResidualVariance);
        }
    }

    [Fact]
    public void Two_independent_random_walks_are_rejected_at_the_five_percent_value_in_at_most_eight_percent_of_1000_seeds()
    {
        var rejected = 0;
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            var rng = new SeededRandom(seed);
            var y = PairsData.Walk(rng, 252);
            var x = PairsData.Walk(rng, 252);

            if (EngleGranger.Test(y, x).Value.RejectsAt5Percent)
            {
                rejected++;
            }
        }

        rejected.ShouldBeLessThanOrEqualTo(80);
    }

    [Fact]
    public void A_cointegrated_pair_with_ar_1_errors_is_rejected_in_at_least_ninety_percent_of_200_seeds()
    {
        var rejected = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var rng = new SeededRandom(seed + 5000);
            var x = PairsData.Walk(rng, 252);
            var u = PairsData.Ar1(rng, 252, 0.5, 0.01);
            var y = x.Select((v, i) => 1.0 + (0.8 * v) + u[i]).ToArray();

            if (EngleGranger.Test(y, x).Value.RejectsAt5Percent)
            {
                rejected++;
            }
        }

        rejected.ShouldBeGreaterThanOrEqualTo(180);
    }

    [Fact]
    public void The_p_value_is_non_decreasing_in_tau_and_inside_zero_one()
    {
        var previous = 0.0;
        for (var tau = -25.0; tau <= 5.0; tau += 0.001)
        {
            var p = MacKinnonTables.PValue(tau);

            p.ShouldBeInRange(0.0, 1.0);
            p.ShouldBeGreaterThanOrEqualTo(previous);
            previous = p;
        }
    }

    [Theory]
    [InlineData(-3.89644, 0.01)]
    [InlineData(-3.33613, 0.05)]
    [InlineData(-3.04445, 0.10)]
    public void The_p_value_at_each_asymptotic_critical_value_is_the_level_within_0_003(double tau, double level)
    {
        MacKinnonTables.PValue(tau).ShouldBe(level, 0.003);
    }

    [Fact]
    public void The_p_value_is_zero_below_tau_min_and_one_above_tau_max()
    {
        MacKinnonTables.PValue(-19.0).ShouldBe(0.0);
        MacKinnonTables.PValue(0.93).ShouldBe(1.0);
        MacKinnonTables.PValue(double.NaN).ShouldBe(double.NaN);
    }

    [Fact]
    public void Benjamini_hochberg_properties_hold_over_random_p_vectors()
    {
        var rng = new SeededRandom(99);
        for (var round = 0; round < 500; round++)
        {
            var m = 1 + rng.Next(40);
            var p = Enumerable.Range(0, m).Select(_ => Math.Pow(PairsData.Uniform(rng), 2.5)).ToArray();

            var outcome = BenjaminiHochberg.Reject(p, 0.05).Value;

            // Rejections are a prefix of the sorted list.
            var order = Enumerable.Range(0, m).OrderBy(i => p[i]).ToArray();
            var flags = order.Select(i => outcome.Rejected[i]).ToArray();
            flags.Take(outcome.K).All(f => f).ShouldBeTrue();
            flags.Skip(outcome.K).Any(f => f).ShouldBeFalse();

            // A test with p = 1 never adds a rejection.
            BenjaminiHochberg.Reject([.. p, 1.0], 0.05).Value.K.ShouldBeLessThanOrEqualTo(outcome.K);

            // Permuting the inputs gives the same rejected set.
            var perm = Enumerable.Range(0, m).OrderBy(_ => rng.NextBits()).ToArray();
            var permuted = BenjaminiHochberg.Reject(perm.Select(i => p[i]).ToArray(), 0.05).Value;
            for (var j = 0; j < m; j++)
            {
                permuted.Rejected[j].ShouldBe(outcome.Rejected[perm[j]]);
            }
        }
    }

    [Fact]
    public void The_same_inputs_give_the_same_bits_on_the_thread_pool()
    {
        var rng = new SeededRandom(3);
        var x = PairsData.Walk(rng, 252);
        var y = x.Select((v, i) => 0.5 + (0.9 * v) + (0.01 * Math.Sin(i))).ToArray();
        var reference = EngleGranger.Test(y, x).Value;
        var results = new EngleGrangerResult[64];

        Parallel.For(0, results.Length, i => results[i] = EngleGranger.Test(y, x).Value);

        foreach (var r in results)
        {
            PairsData.Bits(r.Tau).ShouldBe(PairsData.Bits(reference.Tau));
            PairsData.Bits(r.PValue).ShouldBe(PairsData.Bits(reference.PValue));
            PairsData.Bits(r.Hedge.Beta).ShouldBe(PairsData.Bits(reference.Hedge.Beta));
        }
    }
}
