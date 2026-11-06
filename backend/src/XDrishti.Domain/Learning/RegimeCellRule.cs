namespace XDrishti.Domain.Learning;

/// <summary>
/// The regime rule (CF-10 part 10d-1, spec D9 and AC14, ADR 0220): a regime is a feature or a shrinkage parent, not a hard split, until its cell has 30 trades. Below 30 the cell shrinks to its parent; from 30 it stands alone. Example: four regimes of 60 trades split into
/// four cells of 15, so each shrinks to its parent. The 30 is the same bar as the cell statistics' (<see cref="RegimeBreakdown.MinTrades"/> reads it from here). Pure.
/// </summary>
public static class RegimeCellRule
{
    /// <summary>The trades a regime cell needs to stand alone.</summary>
    public const int StandAloneTrades = 30;

    /// <summary>True when the regime cell has enough trades to stand alone (30 or more).</summary>
    public static bool StandsAlone(int trades) => trades >= StandAloneTrades;

    /// <summary>True when the regime cell must shrink to its parent (fewer than 30 trades, a negative count included).</summary>
    public static bool ShrinksToParent(int trades) => !StandsAlone(trades);
}
