namespace XDrishti.Domain.Paper;

/// <summary>The size of a ticket for one paper account as the review reads it (spec P10-03).</summary>
/// <param name="AccountId">The paper account.</param>
/// <param name="Allocated">The proposed quantity.</param>
/// <param name="Approved">The quantity the owner approved (the proposed one when nothing was changed).</param>
public sealed record ReviewAllocation(Guid AccountId, int Allocated, int Approved);
