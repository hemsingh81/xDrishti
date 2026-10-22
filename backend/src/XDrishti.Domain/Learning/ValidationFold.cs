namespace XDrishti.Domain.Learning;

/// <summary>One purged walk-forward fold (P6-05): the test window and what the train side lost to the purge and the embargo.</summary>
/// <param name="Index">1-based.</param>
/// <param name="TestStart">First day of the test window.</param>
/// <param name="TestEnd">Last day of the test window.</param>
/// <param name="TrainCount">Train rows kept.</param>
/// <param name="TestCount">Test rows.</param>
/// <param name="Purged">Train rows dropped because their label horizon (resolved instant plus one bar) reaches the first decision of the test window.</param>
/// <param name="Embargoed">Train rows dropped because they entered within the embargo before the window.</param>
/// <param name="SkipReason">Why the fold is not used (too few train rows, no test rows); null when it is used.</param>
public sealed record ValidationFold(int Index, DateOnly TestStart, DateOnly TestEnd, int TrainCount, int TestCount, int Purged, int Embargoed, string? SkipReason)
{
    /// <summary>The fold takes part in the comparison.</summary>
    public bool Usable => SkipReason is null;
}
