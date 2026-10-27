using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The exact sign test on discordant pairs (spec P9-09 D3, Miller 2024): only the cases whose result changed count. Two-sided p is <c>min(1, 2 x P(X &lt;= min(b, c)))</c> for X binomial with n = b + c and p 0.5. Never a significance claim from 30 cases:
/// <see cref="Needed"/> says how many one-way pairs a p below 0.05 takes. Pure.
/// </summary>
public static class PairedComparison
{
    /// <summary>Compares the pass flags of the same cases, in the same order, before and now.</summary>
    public static Result<PairedResult> Compare(IReadOnlyList<bool> before, IReadOnlyList<bool> now)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(now);
        if (before.Count != now.Count)
        {
            return Result.Failure<PairedResult>(Error.Validation("eval.not_paired", "the two runs do not cover the same cases"));
        }

        int both = 0, neither = 0, b = 0, c = 0;
        for (var i = 0; i < before.Count; i++)
        {
            if (before[i] && now[i])
            {
                both++;
            }
            else if (!before[i] && !now[i])
            {
                neither++;
            }
            else if (before[i])
            {
                b++;
            }
            else
            {
                c++;
            }
        }

        return Result.Success(new PairedResult(before.Count, both, neither, b, c, PValue(b, c)));
    }

    /// <summary>The exact two-sided p of <paramref name="b"/> against <paramref name="c"/>; 1 when there is no discordant pair.</summary>
    public static double PValue(int b, int c)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfNegative(c);
        var n = b + c;
        if (n == 0)
        {
            return 1;
        }

        var tail = 0.0;
        var k = Math.Min(b, c);
        for (var i = 0; i <= k; i++)
        {
            tail += Math.Exp(LogChoose(n, i) - (n * Math.Log(2)));
        }

        return Math.Min(1, 2 * tail);
    }

    /// <summary>The fewest discordant pairs, all one way, that give a p below <paramref name="alpha"/> (6 for 0.05).</summary>
    public static int Needed(double alpha = 0.05)
    {
        for (var n = 1; n < 1000; n++)
        {
            if (PValue(n, 0) < alpha)
            {
                return n;
            }
        }

        return int.MaxValue;
    }

    /// <summary>The sentence of the compare report.</summary>
    public static string NeededSentence() => $"with this many cases at least {Needed()} discordant pairs, all one way, are needed for p < 0.05";

    private static double LogChoose(int n, int k)
    {
        var sum = 0.0;
        for (var i = 1; i <= k; i++)
        {
            sum += Math.Log(n - k + i) - Math.Log(i);
        }

        return sum;
    }
}
