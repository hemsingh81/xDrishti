using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Dhan;

/// <summary>How many minutes the fetched bars share with the stored ones, and how many of those differ in a price by more than <see cref="OverlapComparison.Tolerance"/>.</summary>
public readonly record struct OverlapResult(long Compared, long Mismatched);

/// <summary>
/// Compares fetched bars with the stored bars of the same minutes (ADR 0016 §7): a mismatch is a price (open, high, low or close) that differs from the stored one
/// by more than 0.05 % of the stored value. Volume is not compared (sources count it differently). A wrong timestamp alignment shows up as mismatches everywhere.
/// </summary>
public static class OverlapComparison
{
    public const double Tolerance = 0.0005;

    /// <summary>Both lists are sorted oldest first.</summary>
    public static OverlapResult Compare(IReadOnlyList<Bar> fetched, IReadOnlyList<Bar> stored)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentNullException.ThrowIfNull(stored);
        long compared = 0, mismatched = 0;
        int i = 0, j = 0;
        while (i < fetched.Count && j < stored.Count)
        {
            var a = fetched[i];
            var b = stored[j];
            if (a.EpochSeconds < b.EpochSeconds)
            {
                i++;
            }
            else if (a.EpochSeconds > b.EpochSeconds)
            {
                j++;
            }
            else
            {
                compared++;
                if (Differs(a.Open, b.Open) || Differs(a.High, b.High) || Differs(a.Low, b.Low) || Differs(a.Close, b.Close))
                {
                    mismatched++;
                }

                i++;
                j++;
            }
        }

        return new OverlapResult(compared, mismatched);
    }

    private static bool Differs(double fetched, double stored) => Math.Abs(fetched - stored) > Tolerance * Math.Abs(stored);
}
