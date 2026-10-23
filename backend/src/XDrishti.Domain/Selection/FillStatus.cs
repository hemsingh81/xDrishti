namespace XDrishti.Domain.Selection;

/// <summary>Where a candidate ended up in the portfolio fill.</summary>
public enum FillStatus
{
    /// <summary>On the plan.</summary>
    Primary,

    /// <summary>Not taken because of the picks before it, but fine on its own: a stand-in if a primary ticket falls away.</summary>
    Reserve,

    /// <summary>Not admissible on its own (or no reserve place left).</summary>
    Rejected,
}
