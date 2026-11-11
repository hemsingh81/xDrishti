namespace XDrishti.Domain.Learning;

/// <summary>The stability of a tier cell's edge over its parent across the two halves of its trades (BK-11, criterion 11).</summary>
/// <param name="Verdict"><see cref="UniverseHoldOuts.Stable"/>, <see cref="UniverseHoldOuts.Unstable"/> or <see cref="UniverseHoldOuts.Insufficient"/>.</param>
/// <param name="FirstN">Tier trades in the first half.</param>
/// <param name="SecondN">Tier trades in the second half.</param>
public sealed record UniverseHoldOut(string Verdict, int FirstN, int SecondN)
{
    /// <summary>True when both halves have the tier's lower bound above the parent's.</summary>
    public bool IsStable => Verdict == UniverseHoldOuts.Stable;
}

/// <summary>
/// The hold-out of BK-11 (the median split of BK-07's <c>HoldOutStability</c>; not the locked hold-out of ADR 0207). The tier's trades are split at the median exit date (the first half exits before it, the second on or after it). In each half the tier's credible lower bound
/// of the win rate, with the parent's win rate as prior, is compared with that of the rest of the parent (its trades that are not the tier's) in the same date range, with the same prior. Stable needs both halves above; a half with fewer than
/// <see cref="MinPerHalf"/> tier trades is insufficient. Counts are plain, not recency weighted. Pure.
/// </summary>
public static class UniverseHoldOuts
{
    /// <summary>Fewest tier trades in each half (as BK-07).</summary>
    public const int MinPerHalf = 30;

    /// <summary>Both halves have the tier's lower bound above the rest of the parent's.</summary>
    public const string Stable = "stable";

    /// <summary>At least one half does not.</summary>
    public const string Unstable = "unstable";

    /// <summary>A half has fewer than <see cref="MinPerHalf"/> trades.</summary>
    public const string Insufficient = "insufficient";

    /// <param name="tier">The tier cell's trades.</param>
    /// <param name="rest">The parent's trades that are not the tier's.</param>
    /// <param name="priorRate">The parent's win rate, the prior of both.</param>
    /// <param name="options">The cell statistics options (strength and credible level).</param>
    public static UniverseHoldOut Evaluate(IReadOnlyList<CellTrade> tier, IReadOnlyList<CellTrade> rest, double priorRate, CellStatsOptions options)
    {
        ArgumentNullException.ThrowIfNull(tier);
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(options);
        if (tier.Count == 0)
        {
            return new UniverseHoldOut(Insufficient, 0, 0);
        }

        var ordered = tier.OrderBy(t => t.ExitDate).ToList();
        var split = ordered[ordered.Count / 2].ExitDate;
        var first = ordered.Where(t => t.ExitDate < split).ToList();
        var second = ordered.Where(t => t.ExitDate >= split).ToList();
        if (first.Count < MinPerHalf || second.Count < MinPerHalf)
        {
            return new UniverseHoldOut(Insufficient, first.Count, second.Count);
        }

        var stable = Beats(first, [.. rest.Where(t => t.ExitDate < split)], priorRate, options) && Beats(second, [.. rest.Where(t => t.ExitDate >= split)], priorRate, options);
        return new UniverseHoldOut(stable ? Stable : Unstable, first.Count, second.Count);
    }

    private static bool Beats(List<CellTrade> tier, List<CellTrade> rest, double priorRate, CellStatsOptions options)
    {
        var (_, tierLower, _) = BetaShrinkage.Posterior(tier.Count(t => t.R > 0), tier.Count, priorRate, options.PriorStrength, options.CredibleLevel);
        var (_, restLower, _) = BetaShrinkage.Posterior(rest.Count(t => t.R > 0), rest.Count, priorRate, options.PriorStrength, options.CredibleLevel);
        return tierLower > restLower;
    }
}
