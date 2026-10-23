namespace XDrishti.Domain.Learning;

/// <summary>Realised R of a set of candidates split by a threshold (P6-13): the kept trades and the skipped trades. Sums are sequential, so they reproduce.</summary>
/// <param name="Kept">Trades the policy takes.</param>
/// <param name="Skipped">Trades the policy skips.</param>
/// <param name="SumRKept">Net R of the kept trades.</param>
/// <param name="SumRSkipped">Net R of the skipped trades (what taking them would have added).</param>
public sealed record MetaPolicyTotals(int Kept, int Skipped, double SumRKept, double SumRSkipped)
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

    /// <summary>The sum of two sets.</summary>
    public MetaPolicyTotals Plus(MetaPolicyTotals other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new MetaPolicyTotals(Kept + other.Kept, Skipped + other.Skipped, SumRKept + other.SumRKept, SumRSkipped + other.SumRSkipped);
    }
}
