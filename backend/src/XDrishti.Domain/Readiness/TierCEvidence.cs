namespace XDrishti.Domain.Readiness;

/// <summary>
/// The hard fail "evidence only from tier C" (spec CF-11 D3; the owner's reading of 2026-11-07, ADR 0225): a strategy is judged on both sources, the Lab out-of-sample trades and the forward trades, and it is a hard fail when either set exists and consists only of tier C trades.
/// A set without trades is not tier C; an unavailable tier is unavailable, never tier C; a set that cannot be read is unavailable. A fault in any set is a fault whatever the other set says; otherwise an unavailable set makes the term unavailable (a hard fail, never a pass), and
/// only sets that are read and not tier C make it clear. Two sets without trades read clear with no evidence: acceptable only because the trade-count rows fail on zero trades (ADR 0225; pinned by No_trades_in_either_set_is_not_tier_C). Pure.
/// </summary>
public static class TierCEvidence
{
    /// <summary>Judges the sets. The evidence text names every set with its counts and says which set triggered the fault.</summary>
    public static (FaultState State, string Evidence) Judge(params TierSet[] sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var lines = sets.Select(s => s.Line).ToList();
        var triggered = sets.Where(s => s.State == TierSetState.AllTierC).Select(s => s.Name).ToList();
        if (triggered.Count > 0)
        {
            return (FaultState.Fault, "hard fail, triggered by: " + string.Join(", ", triggered) + ". " + string.Join("; ", lines) + ".");
        }

        if (sets.Any(s => s.State == TierSetState.Unavailable))
        {
            return (FaultState.Unavailable, "unavailable, which counts as a fail: " + string.Join("; ", lines) + ".");
        }

        return (FaultState.Clear, "clear: " + string.Join("; ", lines) + ".");
    }
}
