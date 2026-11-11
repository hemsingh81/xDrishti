namespace XDrishti.Domain.Lab;

/// <summary>The parts of the number of looks at a strategy family (spec CF-06, D3): the distinct strategy hashes of the runs, the sweep points (variant hashes of their own), the extra candidate exits of a hash's first full test, the re-plans and the nulls (CF-07).</summary>
public sealed record TrialCounts(int RunHashes, int SweepPoints, int ExtraExits, int Replans, int NullRows, int TrainingSweeps = 0, int TrainingSweepTrials = 0, int LinkedSweeps = 0, int UnlinkedSweeps = 0, int SystemReplays = 0)
{
    // TrainingSweeps (ledger rows of kind training_sweep) and TrainingSweepTrials (the variants they swept) are shown beside the count. LinkedSweeps are the sweeps whose swept version this family is, or descends from by the parent hash of its versions (a strategy tuned from the sweep, ADR 0243 point 5, ADR 0249): each distinct swept version is one look (a re-sweep of an unchanged version adds none) and is in Total. UnlinkedSweeps are the family's own swept versions outside that lineage, counted by SQL, shown and not in Total.
    // SystemReplays (SR-01, ADR 0272) is the number of whole-system replays that used a version of the family (the ledger holds one row per version, so the rows of one run are counted once, by the run half of their id). Each replay is one look and is in Total (the stricter choice: repeated replays over one range are the easiest way to fool oneself); it is not a variant, so DistinctHashes, CountVariantsAsync and HasHashAsync leave these rows out.
    /// <summary>The distinct hashes of the family: what <c>CountVariantsAsync</c> has always counted.</summary>
    public int DistinctHashes => RunHashes + SweepPoints;

    /// <summary>The honest number of looks (D3).</summary>
    public int Total => TrialCounter.Total(this);
}
