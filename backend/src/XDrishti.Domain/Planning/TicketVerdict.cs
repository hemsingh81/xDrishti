namespace XDrishti.Domain.Planning;

/// <summary>What the builder decided about one instrument and cell (P7-04).</summary>
public enum TicketVerdict
{
    /// <summary>A conditional ticket: nothing known at the close is false; what is left is watched tomorrow (or the signal already fired).</summary>
    Conditional,

    /// <summary>A part known at the close is false: no ticket.</summary>
    Rejected,

    /// <summary>The setup could not be classified from closed data (data not ready, incomplete session, strategy does not compile): no ticket, never a guess.</summary>
    Unavailable,
}
