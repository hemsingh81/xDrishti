namespace XDrishti.Domain.Lab;

/// <summary>The parts of the number of looks at a strategy family (spec CF-06, D3): the distinct strategy hashes of the runs, the sweep points (variant hashes of their own), the extra candidate exits of a hash's first full test, the re-plans and the nulls (CF-07).</summary>
public sealed record TrialCounts(int RunHashes, int SweepPoints, int ExtraExits, int Replans, int NullRows, int TrainingSweeps = 0, int TrainingSweepTrials = 0)
{
    // TrainingSweeps (ledger rows of kind training_sweep) and TrainingSweepTrials (the variants they swept) are shown beside the count and are not in Total: nothing links a strategy tuned from a sweep to the sweep yet (ADR 0243).
    /// <summary>The distinct hashes of the family: what <c>CountVariantsAsync</c> has always counted.</summary>
    public int DistinctHashes => RunHashes + SweepPoints;

    /// <summary>The honest number of looks (D3).</summary>
    public int Total => TrialCounter.Total(this);
}
