using XDrishti.Domain.Lab;
using XDrishti.Domain.Options.Pricing;

namespace XDrishti.Domain.Tests.Options.Pricing;

/// <summary>Spec P14-05 criteria 2 to 5 and 7: seeded property tests (fixed seed, 10,000 cases).</summary>
public sealed class PricingPropertyTests
{
    private const int Cases = 10_000;
    private const double Hour = 1.0 / (365.0 * 24.0);

    private readonly record struct Case(double S, double K, double Sigma, double T, double R, double Q)
    {
        public double F => S * Math.Exp((R - Q) * T);

        public double DF => Math.Exp(-R * T);
    }

    private static double Uniform(SeededRandom rng, double lo, double hi) => lo + ((hi - lo) * ((rng.NextBits() >> 11) * (1.0 / 9007199254740992.0)));

    private static double LogUniform(SeededRandom rng, double lo, double hi) => Math.Exp(Uniform(rng, Math.Log(lo), Math.Log(hi)));

    private static List<Case> Make(ulong seed, double maxQ = 0.0)
    {
        var rng = new SeededRandom(seed);
        var list = new List<Case>(Cases);
        for (var i = 0; i < Cases; i++)
        {
            var s = LogUniform(rng, 10, 50_000);
            var r = Uniform(rng, 0, 0.15);
            var q = maxQ > 0 ? Uniform(rng, 0, maxQ) : 0.0;
            var sigma = Uniform(rng, 0.02, 2.0);
            var t = LogUniform(rng, Hour, 2.0);
            var c = new Case(s, 0, sigma, t, r, q);
            list.Add(c with { K = c.F * Uniform(rng, 0.5, 1.5) });
        }

        return list;
    }

    [Fact]
    public void Put_call_parity_holds_to_1e_9_relative()
    {
        foreach (var c in Make(14_05_01))
        {
            var call = Black76.Price(OptionKind.Call, c.F, c.K, c.T, c.DF, c.Sigma).Value;
            var put = Black76.Price(OptionKind.Put, c.F, c.K, c.T, c.DF, c.Sigma).Value;

            Math.Abs(call - put - (c.DF * (c.F - c.K))).ShouldBeLessThanOrEqualTo(1e-9 * Math.Max(c.F, c.K), $"{c}");
        }
    }

    [Fact]
    public void Prices_stay_within_the_no_arbitrage_bounds_and_the_greeks_within_their_ranges()
    {
        foreach (var c in Make(14_05_02))
        {
            foreach (var kind in Enum.GetValues<OptionKind>())
            {
                var isCall = kind == OptionKind.Call;
                var price = Black76.Price(kind, c.F, c.K, c.T, c.DF, c.Sigma).Value;
                var lower = c.DF * Math.Max(isCall ? c.F - c.K : c.K - c.F, 0);
                var upper = c.DF * (isCall ? c.F : c.K);
                price.ShouldBeGreaterThanOrEqualTo(lower);
                price.ShouldBeLessThanOrEqualTo(upper);
                var g = Black76.Greeks(kind, c.F, c.K, c.T, c.DF, c.Sigma).Value;
                if (isCall)
                {
                    g.Delta.ShouldBeInRange(0.0, 1.0);
                }
                else
                {
                    g.Delta.ShouldBeInRange(-1.0, 0.0);
                }

                g.Gamma.ShouldBeGreaterThanOrEqualTo(0.0);
                g.Vega.ShouldBeGreaterThanOrEqualTo(0.0);
            }
        }
    }

    [Fact]
    public void Price_strictly_increases_with_volatility_where_vega_is_not_negligible()
    {
        var checkedCases = 0;
        foreach (var c in Make(14_05_03))
        {
            var sigmaUp = c.Sigma * 1.1;
            if (sigmaUp > 5.0 || Black76.RawVega(c.F, c.K, c.T, c.DF, c.Sigma) <= 1e-8 * c.F)
            {
                continue;
            }

            foreach (var kind in Enum.GetValues<OptionKind>())
            {
                var low = Black76.Price(kind, c.F, c.K, c.T, c.DF, c.Sigma).Value;
                var high = Black76.Price(kind, c.F, c.K, c.T, c.DF, sigmaUp).Value;
                high.ShouldBeGreaterThan(low, $"{c}");
            }

            checkedCases++;
        }

        checkedCases.ShouldBeGreaterThan(Cases / 4);
    }

    [Fact]
    public void Greeks_agree_with_central_finite_differences_of_the_price()
    {
        // The step is 1e-4 relative, so the price must vary on a scale larger than the step: the cases with sigma sqrt(T) below 0.05 are skipped (the smile of the price in S is then narrower than 5 percent).
        var tested = 0;
        foreach (var c in Make(14_05_04, maxQ: 0.05))
        {
            if (c.Sigma * Math.Sqrt(c.T) < 0.05)
            {
                continue;
            }

            tested++;
            foreach (var kind in Enum.GetValues<OptionKind>())
            {
                var g = BlackScholes.Greeks(kind, c.S, c.K, c.T, c.R, c.Sigma, c.Q).Value;
                double P(double s, double t, double sg) => BlackScholes.Price(kind, s, c.K, t, c.R, sg, c.Q).Value;
                const double h = 1e-4;
                var hs = h * c.S;
                var delta = (P(c.S + hs, c.T, c.Sigma) - P(c.S - hs, c.T, c.Sigma)) / (2 * hs);
                var gamma = (P(c.S + hs, c.T, c.Sigma) - (2 * P(c.S, c.T, c.Sigma)) + P(c.S - hs, c.T, c.Sigma)) / (hs * hs);
                var hv = h * c.Sigma;
                var vega = (P(c.S, c.T, c.Sigma + hv) - P(c.S, c.T, c.Sigma - hv)) / (2 * hv) / 100.0;
                var ht = h * c.T;
                var theta = -(P(c.S, c.T + ht, c.Sigma) - P(c.S, c.T - ht, c.Sigma)) / (2 * ht) / 365.0;

                Close(g.Delta, delta, "delta", c);
                Close(g.Gamma, gamma, "gamma", c);
                Close(g.Vega, vega, "vega", c);
                Close(g.Theta, theta, "theta", c);
            }
        }

        tested.ShouldBeGreaterThan(Cases / 4);
    }

    private static void Close(double actual, double numeric, string name, Case c)
    {
        var tolerance = Math.Max(1e-8, 1e-5 * Math.Abs(numeric));
        Math.Abs(actual - numeric).ShouldBeLessThanOrEqualTo(tolerance, $"{name} {actual:R} vs {numeric:R} at {c}");
    }

    [Theory]
    [InlineData(14_05_05UL)]
    [InlineData(14_05_15UL)]
    [InlineData(14_05_25UL)]
    [InlineData(14_05_35UL)]
    [InlineData(14_05_45UL)]
    public void The_iv_round_trip_recovers_sigma_to_1e_8_wherever_vega_is_at_least_1e_6_f_and_otherwise_gives_a_closed_reason(ulong seed)
    {
        var rng = new SeededRandom(seed);
        var solved = 0;
        var refused = 0;
        for (var i = 0; i < Cases; i++)
        {
            var sigma = Uniform(rng, 0.05, 1.5);
            var t = Uniform(rng, 1.0 / 365.0, 1.0);
            var f = LogUniform(rng, 10, 50_000);
            var k = f * Uniform(rng, 0.7, 1.3);
            var df = Math.Exp(-Uniform(rng, 0, 0.15) * t);
            var kind = i % 2 == 0 ? OptionKind.Call : OptionKind.Put;
            var price = Black76.Price(kind, f, k, t, df, sigma).Value;
            var identified = Black76.RawVega(f, k, t, df, sigma) >= 1e-6 * f;
            var result = ImpliedVol.Solve(kind, price, f, k, t, df);

            if (result.IsAvailable)
            {
                result.Value.ShouldBeInRange(ImpliedVol.LowVol, ImpliedVol.HighVol);
                if (identified)
                {
                    Math.Abs(result.Value - sigma).ShouldBeLessThanOrEqualTo(1e-8, $"sigma {sigma:R} F {f:R} K {k:R} T {t:R} {kind}");
                }

                solved++;
            }
            else
            {
                identified.ShouldBeFalse($"a well identified price was refused ({result.Reason}) sigma {sigma:R} F {f:R} K {k:R} T {t:R} {kind}");
                refused++;
            }
        }

        solved.ShouldBeGreaterThan(Cases / 2);
        (solved + refused).ShouldBe(Cases);
    }

    [Fact]
    public void Repeated_calls_and_a_thread_pool_run_give_identical_bits()
    {
        var cases = Make(14_05_07);
        static long[] Run(Case c)
        {
            var price = Black76.Price(OptionKind.Call, c.F, c.K, c.T, c.DF, c.Sigma).Value;
            var g = Black76.Greeks(OptionKind.Put, c.F, c.K, c.T, c.DF, c.Sigma).Value;
            var iv = ImpliedVol.Solve(OptionKind.Call, price, c.F, c.K, c.T, c.DF);
            return [BitConverter.DoubleToInt64Bits(price), BitConverter.DoubleToInt64Bits(g.Delta), BitConverter.DoubleToInt64Bits(g.Gamma), BitConverter.DoubleToInt64Bits(g.Vega), BitConverter.DoubleToInt64Bits(g.Theta), iv.IsAvailable ? BitConverter.DoubleToInt64Bits(iv.Value) : (long)iv.Reason!.Value];
        }

        var sequential = cases.Take(2000).Select(Run).ToArray();
        var again = cases.Take(2000).Select(Run).ToArray();
        var parallel = new long[2000][];
        Parallel.For(0, 2000, i => parallel[i] = Run(cases[i]));

        for (var i = 0; i < 2000; i++)
        {
            again[i].ShouldBe(sequential[i]);
            parallel[i].ShouldBe(sequential[i]);
        }

    }
}
