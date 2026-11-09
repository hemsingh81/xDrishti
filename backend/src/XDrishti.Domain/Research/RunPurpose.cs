namespace XDrishti.Domain.Research;

/// <summary>
/// Why a stored run exists (spec AU-11, ADR 0243). <see cref="Research"/> is every run so far and the default. <see cref="Training"/> is bulk simulation used to learn from: it ends before the hold-out block, and no evidence reader (Lab verdicts, the scorecard,
/// cell statistics, the ledger counts, model promotion) ever sees it. It becomes evidence only through a normal Lab post that meets the existing rules; there is no conversion.
/// </summary>
public enum RunPurpose
{
    Research = 0,
    Training = 1,
}
