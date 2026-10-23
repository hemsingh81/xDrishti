namespace XDrishti.Domain.Planning;

/// <summary>One fill with what the journal made of it (P7-14, ADR 0093).</summary>
/// <param name="Fill">The stored fill.</param>
/// <param name="Role">Entry, exit or discretionary.</param>
/// <param name="ItemId">The ticket, null for a discretionary fill.</param>
/// <param name="AccountId">The account of the ticket's allocation (the fill's own account).</param>
/// <param name="Qty">Shares of the fill that belong to the role (an exit may use part of a fill).</param>
/// <param name="Reason">Why, in one sentence.</param>
public sealed record JournalLink(JournalFill Fill, JournalRole Role, long? ItemId, Guid AccountId, long Qty, string Reason);
