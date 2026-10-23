namespace XDrishti.Domain.Learning;

/// <summary>One test window of an experiment (P6-11): the mean R of each run on the trades entered in the window.</summary>
/// <param name="Index">1-based.</param>
/// <param name="TestStart">First day.</param>
/// <param name="TestEnd">Last day.</param>
/// <param name="BaselineTrades">Baseline trades entered in the window.</param>
/// <param name="CandidateTrades">Candidate trades entered in the window.</param>
/// <param name="BaselineMeanR">Mean R of the baseline trades; null when there are none.</param>
/// <param name="CandidateMeanR">Mean R of the candidate trades; null when there are none.</param>
/// <param name="SkipReason">Why the fold is not used; null when it is used.</param>
/// <param name="Better">The candidate's mean R is above the baseline's by more than the required improvement (false for an unused fold).</param>
public sealed record ExperimentFold(int Index, DateOnly TestStart, DateOnly TestEnd, int BaselineTrades, int CandidateTrades, double? BaselineMeanR, double? CandidateMeanR, string? SkipReason, bool Better)
{
    /// <summary>The fold takes part in the verdict.</summary>
    public bool Usable => SkipReason is null;

    /// <summary>Candidate mean minus baseline mean; null when a side is empty.</summary>
    public double? Difference => CandidateMeanR - BaselineMeanR;
}
