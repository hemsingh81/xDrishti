namespace XDrishti.Domain.Portfolio;

/// <summary>What a sync found out about one holding.</summary>
public enum HoldingChange
{
    /// <summary>Only the price (and the broker's average cost) moved.</summary>
    Unchanged,

    /// <summary>A holding the account did not have: new, or bought again after it was closed.</summary>
    Added,

    /// <summary>More shares than before: a lot dated today was added.</summary>
    Increased,

    /// <summary>Fewer shares than before: lots were closed oldest first.</summary>
    Decreased,

    /// <summary>The broker no longer lists the holding: everything was closed.</summary>
    Closed,
}
