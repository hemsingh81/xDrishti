namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>How a candidate stands against its basket's risk budget.</summary>
public enum BudgetFitOutcome
{
    /// <summary>The candidate's risk fits in the room: no change.</summary>
    Fits,

    /// <summary>The candidate is scaled down to the room (the ratio is at or above the floor).</summary>
    Scaled,

    /// <summary>The ratio is below the floor, or there is no room: the candidate goes to the reserve.</summary>
    Reserve,
}
