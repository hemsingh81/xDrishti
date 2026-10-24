namespace XDrishti.Domain.Planning;

/// <summary>The owner's lower size for one account of a ticket (spec P7-10).</summary>
/// <param name="AccountId">The account the ticket is already sized for.</param>
/// <param name="Quantity">The new quantity, at least 1 and below the allocated one.</param>
public sealed record QuantityChange(Guid AccountId, int Quantity);
