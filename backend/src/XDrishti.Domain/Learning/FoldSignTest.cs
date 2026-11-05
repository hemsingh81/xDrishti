namespace XDrishti.Domain.Learning;

/// <summary>
/// The fold sign test (CF-10 part 10c-1, ADR 0216, spec D4 and AC6), shown beside the bootstrap: 5 of 6 folds better gives <c>p = 7 / 64 = 0.109375</c>, 6 of 6 gives <c>1 / 64 = 0.015625</c>. A fold is better when the gain is above zero;
/// a fold with a gain of exactly zero, or one whose gain is not a finite number, is left out and counted with the ties. The one-sided p-value is the exact binomial tail at one half, <c>sum over k from Better to n of C(n, k) / 2^n</c>. The folds are few and correlated in time, so this is a plain look, not a verdict. Pure.
/// </summary>
public static class FoldSignTest
{
    private const int MaxFolds = 1000;

    /// <summary>The test over the gain of each fold (baseline loss minus model loss).</summary>
    public static SignTestResult Assess(IReadOnlyList<double> foldGains)
    {
        ArgumentNullException.ThrowIfNull(foldGains);
        int better = 0, worse = 0, ties = 0;
        foreach (var g in foldGains)
        {
            if (!double.IsFinite(g) || g == 0)
            {
                ties++;
            }
            else if (g > 0)
            {
                better++;
            }
            else
            {
                worse++;
            }
        }

        return new SignTestResult(better, worse, ties, PValue(better, better + worse));
    }

    /// <summary>The chance of at least <paramref name="better"/> of <paramref name="folds"/> coin tosses coming up heads; null for no folds or more than 1000.</summary>
    public static double? PValue(int better, int folds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(better);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(better, folds);
        if (folds is 0 or > MaxFolds)
        {
            return null;
        }

        // C(n, k) as doubles stays exact for small n and finite up to n = 1000 (C(1000, 500) is about 2.7e299); the tail is scaled by 2^-n at the end.
        double c = 1, tail = 0;
        for (var k = 0; k <= folds; k++)
        {
            if (k >= better)
            {
                tail += c;
            }

            c = c * (folds - k) / (k + 1);
        }

        return Math.ScaleB(tail, -folds);
    }
}
