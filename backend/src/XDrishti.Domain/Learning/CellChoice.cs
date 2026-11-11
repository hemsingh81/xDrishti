namespace XDrishti.Domain.Learning;

/// <summary>Which cell a reader takes (BK-11, criterion 6) and why.</summary>
/// <param name="Cell">The chosen row: the child or the parent.</param>
/// <param name="Reason"><see cref="CellChoice.ChildBetter"/>, <see cref="CellChoice.NoBetterThanParent"/> or <see cref="CellChoice.ChildThin"/>.</param>
/// <param name="ChildLower">The child's lower credible bound of the win rate (as stored).</param>
/// <param name="ParentLower">The lower bound of the parent without the child's trades (leave-one-out), null when the child is thin and the comparison was not made.</param>
public sealed record CellChoiceResult(CellStatsRow Cell, string Reason, double ChildLower, double? ParentLower)
{
    /// <summary>True when the child was chosen.</summary>
    public bool IsChild => Reason == CellChoice.ChildBetter;
}

/// <summary>
/// The parent-beats rule (BK-11, ADR 0261): a tier cell is read only when its credible lower bound is above that of its <c>*</c> parent with the child's own trades taken out (the child is a part of the parent, so comparing with the whole would flatter
/// neither side; the review fix of 2026-10-25, exclusion method). The bound of the parent without the child is the Beta bound of the weighted wins and trades left, with the parent's own prior. Pure; it relabels no stored row, and in this slice only reports read
/// it: a decision path reads the <c>*</c> cell (ADR 0193, owner question 2).
/// </summary>
public static class CellChoice
{
    /// <summary>The child is read: it has enough trades and its lower bound is above the parent's.</summary>
    public const string ChildBetter = "child_better";

    /// <summary>The child has enough trades but its lower bound is not above the parent's: "no better than all shares".</summary>
    public const string NoBetterThanParent = "no_better_than_parent";

    /// <summary>The child has fewer trades than the minimum (or is not <see cref="CellStatsRow.Enough"/>): "not enough evidence"; the parent is read.</summary>
    public const string ChildThin = "child_thin";

    /// <summary>Chooses between a tier cell and its parent in one window.</summary>
    public static CellChoiceResult Resolve(CellStatsRow child, CellStatsRow parent, int minTrades, CellStatsOptions options)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(options);
        if (child.N < minTrades || !child.Enough)
        {
            return new CellChoiceResult(parent, ChildThin, child.WinRateLower, null);
        }

        var s = options.PriorStrength;
        var parentWins = (parent.WinRatePosterior * (parent.EffectiveN + s)) - (s * parent.PriorRate);
        var childWins = (child.WinRatePosterior * (child.EffectiveN + s)) - (s * child.PriorRate);
        var restWins = Math.Max(0, parentWins - childWins);
        var restTotal = Math.Max(0, parent.EffectiveN - child.EffectiveN);
        var (_, restLower, _) = BetaShrinkage.Posterior(restWins, restTotal, parent.PriorRate, s, options.CredibleLevel);
        return child.WinRateLower > restLower
            ? new CellChoiceResult(child, ChildBetter, child.WinRateLower, restLower)
            : new CellChoiceResult(parent, NoBetterThanParent, child.WinRateLower, restLower);
    }
}
