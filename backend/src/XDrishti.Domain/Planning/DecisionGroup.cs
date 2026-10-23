namespace XDrishti.Domain.Planning;

/// <summary>What the owner (or the cutoff) did with a ticket, read from its newest state (P7-13, ADR 0090). There is no auto-approved group: nothing approves on the owner's behalf.</summary>
public enum DecisionGroup
{
    /// <summary>Approved, and whatever followed (armed, triggered, closed, invalidated, expired).</summary>
    Approved,

    /// <summary>Modified by the owner and not approved after.</summary>
    Modified,

    /// <summary>Rejected by the owner.</summary>
    Rejected,

    /// <summary>Nobody decided before the review cutoff.</summary>
    Lapsed,

    /// <summary>Still proposed: no decision recorded.</summary>
    Undecided,
}

/// <summary>Reading a ticket state as a decision group.</summary>
public static class DecisionGroups
{
    /// <summary>The group of a state.</summary>
    public static DecisionGroup Of(TicketState state) => state switch
    {
        TicketState.Approved or TicketState.Armed or TicketState.Invalidated or TicketState.Expired or TicketState.Triggered or TicketState.Closed => DecisionGroup.Approved,
        TicketState.Modified => DecisionGroup.Modified,
        TicketState.Rejected => DecisionGroup.Rejected,
        TicketState.Lapsed => DecisionGroup.Lapsed,
        _ => DecisionGroup.Undecided,
    };
}
