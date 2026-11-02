namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The outcome of the one hold-out reading (P13-05).</summary>
public enum ExtensionHoldOutStatus
{
    /// <summary>The gain has the same sign and B's log loss is not above A's.</summary>
    Confirmed,

    /// <summary>The hold-out disagrees.</summary>
    NotConfirmed,

    /// <summary>Too few hold-out rows, or a threshold could not be chosen.</summary>
    NotEvaluable,
}
