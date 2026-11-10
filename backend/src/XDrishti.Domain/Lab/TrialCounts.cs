namespace XDrishti.Domain.Lab;

/// <summary>The parts of the number of looks at a strategy family (spec CF-06, D3): the distinct strategy hashes of the runs, the sweep points (variant hashes of their own), the extra candidate exits of a hash's first full test, the re-plans and the nulls (CF-07).</summary>
public sealed record TrialCounts(int RunHashes, int SweepPoints, int ExtraExits, int Replans, int NullRows, int TrainingSweeps = 0, int TrainingSweepTrials = 0, int LinkedSweeps = 0, int UnlinkedSweeps = 0)
{
    // TrainingSweeps (ledger rows of kind training_sweep) and TrainingSweepTrials (the variants they swept) are shown beside the count. LinkedSweeps are the sweeps whose swept version this family is, or descends from by the parent hash of its versions (a strategy tuned from the sweep, ADR 0243 point 5, ADR 0249): each distinct swept version is one look (a re-sweep of an unchanged version adds none) and is in Total. UnlinkedSweeps are the family's own swept versions outside that lineage, counted by SQL, shown and not in Total.
    /// <summary>The distinct hashes of the family: what <c>CountVariantsAsync</c> has always counted.</summary>
    public int DistinctHashes => RunHashes + SweepPoints;

    /// <summary>The honest number of looks (D3).</summary>
    public int Total => TrialCounter.Total(this);
}
