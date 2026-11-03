using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Pairs;

/// <summary>Seeded series for the pairs tests (SplitMix64 through <see cref="SeededRandom"/>, Box-Muller for the normals).</summary>
internal static class PairsData
{
    internal static double Uniform(SeededRandom rng) => (((rng.NextBits() >> 11) + 0.5) * (1.0 / 9007199254740992.0));

    internal static double Normal(SeededRandom rng) => Math.Sqrt(-2.0 * Math.Log(Uniform(rng))) * Math.Cos(2.0 * Math.PI * Uniform(rng));

    /// <summary>A Gaussian random walk of <paramref name="n"/> points starting at 0 (the first point is 0).</summary>
    internal static double[] Walk(SeededRandom rng, int n, double step = 0.01)
    {
        var w = new double[n];
        for (var i = 1; i < n; i++)
        {
            w[i] = w[i - 1] + (step * Normal(rng));
        }

        return w;
    }

    internal static double[] Ar1(SeededRandom rng, int n, double rho, double sigma)
    {
        var u = new double[n];
        u[0] = sigma * Normal(rng) / Math.Sqrt(1 - (rho * rho));
        for (var i = 1; i < n; i++)
        {
            u[i] = (rho * u[i - 1]) + (sigma * Normal(rng));
        }

        return u;
    }

    internal static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
}
