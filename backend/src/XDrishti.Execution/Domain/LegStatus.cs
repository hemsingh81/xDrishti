namespace XDrishti.Execution.Domain;

public enum LegKind
{
    Entry,
    Stop,
    Target,
    Exit,
    Flatten,
}

/// <summary>Broker leg statuses. The first six are ordered by <see cref="Leg.Rank"/>; the last four are final.</summary>
public enum LegStatus
{
    New,
    Sent,
    Transit,
    Pending,
    PartTraded,
    Traded,
    Cancelled,
    Rejected,
    Expired,

    /// <summary>A super order whose entry and one exit leg both completed.</summary>
    Closed,
}
