namespace XDrishti.Api.Endpoints;

/// <summary>The owner's "approve despite" for one account: the check codes named and why. The ticket's own symbol is bound by the server; the owner is never part of any body.</summary>
/// <param name="AccountId">The account the confirmation is for.</param>
/// <param name="Codes">The check codes approved despite (never the account gate).</param>
/// <param name="Reason">Why, 1 to 200 characters.</param>
internal sealed record CheckConfirmationRequest(Guid AccountId, IReadOnlyList<string>? Codes, string? Reason);

/// <summary>The body of an approval. Everything is optional; a ticket whose checks all passed needs no body. The actor comes from the signed-in session, never from here.</summary>
/// <param name="Reason">Why, 1 to 500 characters; a default sentence when omitted.</param>
/// <param name="Confirmations">The checks approved despite, per account.</param>
internal sealed record ApproveTicketRequest(string? Reason, IReadOnlyList<CheckConfirmationRequest>? Confirmations);

/// <summary>A lower size for one account of the ticket.</summary>
/// <param name="AccountId">The account the ticket is already sized for.</param>
/// <param name="Quantity">The new quantity (at least 1, below the allocated one).</param>
internal sealed record QuantityChangeRequest(Guid AccountId, int Quantity);

/// <summary>The body of a modification: sizes can only be reduced; nothing else can be edited.</summary>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Quantities">The new quantities.</param>
internal sealed record ModifyTicketRequest(string? Reason, IReadOnlyList<QuantityChangeRequest>? Quantities);

/// <summary>The body of a rejection.</summary>
/// <param name="Reason">Why, 1 to 500 characters.</param>
internal sealed record RejectTicketRequest(string? Reason);
