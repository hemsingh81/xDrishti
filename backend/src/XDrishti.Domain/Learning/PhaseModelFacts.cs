namespace XDrishti.Domain.Learning;

/// <summary>What the registry stores about the model the phase check judges (P6-18): the champion if there is one, else the newest challenger. Copied from the stored record; nothing is computed.</summary>
/// <param name="Id">The registry id.</param>
/// <param name="Selection">Why this model: <c>champion</c> or <c>newest challenger</c>.</param>
/// <param name="Verdict">The validation verdict stored at registration.</param>
/// <param name="UsedFolds">Folds the verdict used.</param>
/// <param name="FoldsBeatingBoth">Folds in which the model beat both baselines.</param>
/// <param name="FoldsNeeded">Folds it had to win.</param>
/// <param name="PooledBeatsBoth">It beat both baselines on the pooled rows.</param>
/// <param name="PooledEcePp">Pooled expected calibration error, percentage points.</param>
/// <param name="HoldOutStart">First day of the kept hold-out, or null when none was kept.</param>
/// <param name="From">Earliest trade date of the training data.</param>
/// <param name="To">Latest trade date of the training data.</param>
/// <param name="Reasons">The stored reasons it is not eligible; empty when eligible.</param>
/// <param name="ChampionReasons">The reasons the record does not allow the role champion (<see cref="ChampionRule"/>); empty when it does.</param>
public sealed record PhaseModelFacts(
    long Id,
    string Selection,
    ValidationOutcome Verdict,
    int UsedFolds,
    int FoldsBeatingBoth,
    int FoldsNeeded,
    bool PooledBeatsBoth,
    double PooledEcePp,
    DateOnly? HoldOutStart,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> ChampionReasons);
