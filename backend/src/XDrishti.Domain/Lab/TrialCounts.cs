namespace XDrishti.Domain.Lab;

/// <summary>The parts of the number of looks at a strategy family (spec CF-06, D3): the distinct strategy hashes of the runs, the sweep points (variant hashes of their own), the extra candidate exits of a hash's first full test, the re-plans and the nulls (CF-07).</summary>
public sealed record TrialCounts(int RunHashes, int SweepPoints, int ExtraExits, int Replans, int NullRows)
{
    /// <summary>The distinct hashes of the family: what <c>CountVariantsAsync</c> has always counted.</summary>
    public int DistinctHashes => RunHashes + SweepPoints;

    /// <summary>The honest number of looks (D3).</summary>
    public int Total => TrialCounter.Total(this);
}
