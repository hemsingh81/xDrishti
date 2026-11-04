namespace XDrishti.Domain.Lab;

/// <summary>
/// The honest number of looks (spec CF-06, D3): N = distinct strategy hashes (sweep points are hashes) + one per extra exit candidate of each hash's first full test + one per re-plan + one per null row. The factor stays the heuristic of <see cref="MultipleTesting"/>; this only makes N honest.
/// </summary>
public static class TrialCounter
{
    /// <summary>N for the parts; a part below zero counts as zero.</summary>
    public static int Total(TrialCounts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return checked(Math.Max(0, parts.RunHashes) + Math.Max(0, parts.SweepPoints) + Math.Max(0, parts.ExtraExits) + Math.Max(0, parts.Replans) + Math.Max(0, parts.NullRows));
    }

    /// <summary>The extra looks of a first full test with <paramref name="candidates"/> exits: one less than the candidates, never below zero.</summary>
    public static int ExtraExits(int candidates) => Math.Max(0, candidates - 1);

    /// <summary>The count the verdict is judged with: the honest total when <paramref name="honest"/>, else the old distinct-hash count (the owner's switch; nothing changes until it is on).</summary>
    public static int Judged(TrialCounts parts, bool honest) => honest ? parts.Total : parts.DistinctHashes;
}
