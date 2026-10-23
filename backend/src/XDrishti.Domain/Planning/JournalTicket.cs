using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>
/// A ticket the owner approved, for one paper account it was allocated to: the only thing a fill can be matched to (P7-14, ADR 0093).
/// </summary>
/// <param name="ItemId">The plan item.</param>
/// <param name="PlanId">The plan.</param>
/// <param name="Rank">The plan rank (orders tickets deterministically).</param>
/// <param name="AccountId">The account of the allocation.</param>
/// <param name="Symbol">The normalised symbol.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="ApprovedAt">When the ticket was approved; a fill before it is not this ticket's.</param>
public sealed record JournalTicket(long ItemId, long PlanId, int Rank, Guid AccountId, string Symbol, Side Side, DateOnly ForDate, DateTimeOffset ApprovedAt);
