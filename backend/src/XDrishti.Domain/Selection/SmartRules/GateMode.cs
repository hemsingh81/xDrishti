namespace XDrishti.Domain.Selection;

/// <summary>Market gate mode (doc 09 section 14).</summary>
public enum GateMode
{
    /// <summary>No change.</summary>
    Normal,

    /// <summary>Half the items, risk x 0.75.</summary>
    Reduced,

    /// <summary>No tickets; candidates go to reserve.</summary>
    NoTrade,

    /// <summary>Not known. No change to the plan, but the plan is flagged: it is not Normal.</summary>
    Unavailable,
}
