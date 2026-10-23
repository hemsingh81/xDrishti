namespace XDrishti.Domain.Learning;

/// <summary>Realised R of a set of candidates split by a threshold (P6-13): the kept trades and the skipped trades. Sums are sequential, so they reproduce.</summary>
/// <param name="Kept">Trades the policy takes.</param>
/// <param name="Skipped">Trades the policy skips.</param>
/// <param name="SumRKept">Net R of the kept trades.</param>
/// <param name="SumRSkipped">Net R of the skipped trades (what taking them would have added).</param>
/// <param name="SumSqKept">Sum of squared R of the kept trades (for the spread).</param>
/// <param name="SumSqSkipped">Sum of squared R of the skipped trades.</param>
public sealed record MetaPolicyTotals(int Kept, int Skipped, double SumRKept, double SumRSkipped, double SumSqKept = 0, double SumSqSkipped = 0)
{
    /// <summary>Nothing counted.</summary>
    public static MetaPolicyTotals Empty { get; } = new(0, 0, 0, 0);

    /// <summary>All candidates.</summary>
    public int Candidates => Kept + Skipped;

    /// <summary>Share of the candidates taken; 1 when there are none.</summary>
    public double KeptShare => Candidates == 0 ? 1 : Kept / (double)Candidates;

    /// <summary>Mean R of taking everything.</summary>
    public double BaselinePerCandidate => Candidates == 0 ? 0 : (SumRKept + SumRSkipped) / Candidates;

    /// <summary>Net R of the kept trades per candidate (the skipped add nothing).</summary>
    public double PolicyPerCandidate => Candidates == 0 ? 0 : SumRKept / Candidates;

    /// <summary>Policy minus take-everything, in R per candidate; equal to minus the mean contribution of the skipped trades.</summary>
    public double Lift => PolicyPerCandidate - BaselinePerCandidate;

    /// <summary>Mean R of the kept trades, or null when none.</summary>
    public double? MeanKept => Kept == 0 ? null : SumRKept / Kept;

    /// <summary>Mean R of the skipped trades, or null when none.</summary>
    public double? MeanSkipped => Skipped == 0 ? null : SumRSkipped / Skipped;

    /// <summary>One candidate: kept or skipped, with its R.</summary>
    public static MetaPolicyTotals One(bool kept, double r) => kept ? new MetaPolicyTotals(1, 0, r, 0, r * r, 0) : new MetaPolicyTotals(0, 1, 0, r, 0, r * r);

    /// <summary>
    /// What skipping the same share of the candidates at random would add per candidate: minus the skipped share times the take-everything mean. Positive when taking everything loses money,
    /// which is why a lift alone proves nothing there.
    /// </summary>
    public double RandomLift => Candidates == 0 ? 0 : -(Skipped / (double)Candidates) * BaselinePerCandidate;

    /// <summary>The lift beyond what a random skip of the same share would give: the part that is skill.</summary>
    public double ExcessLift => Lift - RandomLift;

    /// <summary>Mean R of the kept minus mean R of the skipped trades; null when either side is empty.</summary>
    public double? Skill => MeanKept is { } k && MeanSkipped is { } s ? k - s : null;

    /// <summary>Standard error of <see cref="Skill"/> (unpooled variances, sample variance); null when a side has fewer than two trades.</summary>
    public double? SkillStandardError
    {
        get
        {
            if (Kept < 2 || Skipped < 2)
            {
                return null;
            }

            var variance = Variance(Kept, SumRKept, SumSqKept) / Kept;
            variance += Variance(Skipped, SumRSkipped, SumSqSkipped) / Skipped;
            return Math.Sqrt(variance);
        }
    }

    /// <summary>The sum of two sets.</summary>
    public MetaPolicyTotals Plus(MetaPolicyTotals other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new MetaPolicyTotals(Kept + other.Kept, Skipped + other.Skipped, SumRKept + other.SumRKept, SumRSkipped + other.SumRSkipped, SumSqKept + other.SumSqKept, SumSqSkipped + other.SumSqSkipped);
    }

    private static double Variance(int n, double sum, double sumSq) => Math.Max(0, (sumSq - (sum * sum / n)) / (n - 1));
}
