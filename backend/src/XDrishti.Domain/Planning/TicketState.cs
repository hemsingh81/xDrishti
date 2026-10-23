namespace XDrishti.Domain.Planning;

/// <summary>
/// Where a plan ticket stands (spec P7-03, ADR 0083; the diagram of doc 04 §4.2). The first state, Proposed, has no row: a ticket is Proposed until a state row says otherwise.
/// Armed means "watched in our own database and the live active set"; it never means that an order rests at a broker.
/// </summary>
public enum TicketState
{
    /// <summary>The nightly pipeline proposed it; nobody has looked.</summary>
    Proposed,

    /// <summary>The owner edited size, stop or account; it must be re-validated before it can be approved.</summary>
    Modified,

    /// <summary>The owner approved it. Only the owner can.</summary>
    Approved,

    /// <summary>The owner rejected it. Final.</summary>
    Rejected,

    /// <summary>Not reviewed by the cutoff. The default and final: lapsing is never approval.</summary>
    Lapsed,

    /// <summary>The pre-open check passed: the instrument is watched with alerts. No order exists.</summary>
    Armed,

    /// <summary>A gap or event at the pre-open check made it void. Final.</summary>
    Invalidated,

    /// <summary>The validity window ended. Final.</summary>
    Expired,

    /// <summary>The trigger condition was met (an alert, not an order).</summary>
    Triggered,

    /// <summary>Exited, squared off or at max hold. Final.</summary>
    Closed,
}

/// <summary>The spelling of a <see cref="TicketState"/> in the tables and the API.</summary>
public static class TicketStateText
{
    /// <summary>The lower-case code.</summary>
    public static string Code(this TicketState state) => state.ToString().ToLowerInvariant();

    /// <summary>Reads a code back; null when unknown.</summary>
    public static TicketState? TryParse(string? code) =>
        code is not null && Enum.GetValues<TicketState>().Cast<TicketState?>().FirstOrDefault(s => string.Equals(s!.Value.Code(), code, StringComparison.OrdinalIgnoreCase)) is { } found ? found : null;
}
