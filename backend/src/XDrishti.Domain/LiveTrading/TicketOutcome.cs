namespace XDrishti.Domain.LiveTrading;

/// <summary>The one stated outcome of a ticket (spec P11-01 decision D5), written once. <see cref="RejectText"/> is the broker's own, length-capped text for a rejected order and is data, never markup.</summary>
public sealed record TicketOutcome(
    string Outcome,
    long? OrderedQty,
    long EntryQty,
    DateTimeOffset? FirstEntryAt,
    IReadOnlyList<string> Flags,
    double? MissedR,
    string PlanGradeStatus,
    string OrderEvidence,
    string Protection,
    string? RejectText);
