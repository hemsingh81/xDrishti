namespace XDrishti.Domain.Lab;

/// <summary>One Lab test of a strategy on a basket, as the ledger and the run record it (BK-11).</summary>
/// <param name="StrategyHash">The hash of the strategy version tested (its parameters are in it).</param>
/// <param name="BasketId">The basket tested.</param>
/// <param name="From">The window start.</param>
/// <param name="To">The window end.</param>
public sealed record UniverseTrial(string StrategyHash, Guid BasketId, DateOnly From, DateOnly To);

/// <summary>The count of baskets a strategy was tested on, read from the stored tests and not from anyone's memory. An information breakdown of the looks: it never feeds <see cref="MultipleTesting.Factor"/>; the honest count of ADR 0207 (<see cref="TrialCounter"/>) does, and already counts a new universe as a re-plan.</summary>
/// <param name="Baskets">Distinct baskets tested (k).</param>
/// <param name="Tests">Distinct tests: one per strategy hash, basket and window; a repeated one counts once.</param>
/// <param name="ExtraVariants">k - 1: the variants the baskets add to the one the strategy has anyway.</param>
public sealed record UniverseTrialCount(int Baskets, int Tests, int ExtraVariants);

/// <summary>The trial count of strategy x basket runs (BK-11, criterion 9). Pure. The honest count of looks (<see cref="TrialCounter"/>) already adds a re-plan row when a basket changes; this is the figure for baskets alone.</summary>
public static class UniverseTrials
{
    /// <summary>k distinct baskets give k variants from a base of one: k - 1 extra (information only).</summary>
    public static UniverseTrialCount Count(IEnumerable<UniverseTrial> trials)
    {
        ArgumentNullException.ThrowIfNull(trials);
        var distinct = trials.Distinct().ToList();
        var k = distinct.Select(t => t.BasketId).Distinct().Count();
        return new UniverseTrialCount(k, distinct.Count, Math.Max(0, k - 1));
    }
}
