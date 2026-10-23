namespace XDrishti.Domain.Learning;

/// <summary>What a cell's entry-window search honestly ended in (P6-14).</summary>
public enum EntryWindowStatus
{
    /// <summary>The cell has fewer trades than a window needs.</summary>
    NotEnoughEvidence,

    /// <summary>No window with enough trades, a lift and no hole, or the window did not hold in enough folds: keep the strategy's own time rules.</summary>
    NoStableWindow,

    /// <summary>A window was found but too few folds could test it, or fewer folds exist than a verdict needs: a hypothesis only.</summary>
    Inconclusive,

    /// <summary>A window with evidence that held in enough folds: still a proposal, never applied by this slice.</summary>
    Proposed,
}
