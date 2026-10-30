namespace XDrishti.Domain.Execution.Confirm;

/// <summary>
/// The four ways a ticket ends (spec P12-08 Data), each stored once: the primary key of <c>execution.ticket_outcomes</c> on the ticket is the one-decision guarantee. <c>confirmed</c> and <c>skipped</c> are the owner's (written by the API),
/// <c>expired</c> and <c>refused</c> the system's (written by the execution service); a skip is a decision ("declined"), not an error.
/// </summary>
public static class TicketOutcomes
{
    public const string Confirmed = "confirmed";
    public const string Skipped = "skipped";
    public const string Expired = "expired";
    public const string Refused = "refused";

    /// <summary>The state of a ticket without an outcome.</summary>
    public const string Open = "open";

    public const string ExpiryActor = "system:confirm-expiry";
    public const string TicketActor = "system:confirm-ticket";

    /// <summary>The stored reason of the owner's two outcomes.</summary>
    public const string TapReason = "owner_tap";
    public const string DeclinedReason = "declined";

    /// <summary>The stored reason of an expiry.</summary>
    public const string TtlReason = "ttl";

    public static readonly IReadOnlyList<string> All = [Confirmed, Skipped, Expired, Refused];

    public static bool IsOwnerActor(string? actor) => actor is { Length: > 6 } a && a.StartsWith("owner:", StringComparison.Ordinal);
}
