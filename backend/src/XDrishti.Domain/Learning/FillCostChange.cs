namespace XDrishti.Domain.Learning;

/// <summary>How the candidate run's slippage compares with the baseline's, read from the two stored fill rules and not from what was typed (P6-11, ADR 0072).</summary>
public enum FillCostChange
{
    /// <summary>The knob is not a slippage: no cost direction applies.</summary>
    NotApplicable,

    /// <summary>Every slippage component of the candidate is equal or higher, and one is higher.</summary>
    Dearer,

    /// <summary>One or more slippage components of the candidate are lower and none is higher.</summary>
    Cheaper,

    /// <summary>Some components are lower and some higher: they cannot be ordered, and a lower one could win unseen, so it is treated as cheaper.</summary>
    Mixed,
}
