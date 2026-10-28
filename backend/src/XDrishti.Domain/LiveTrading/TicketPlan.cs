using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>What the classifier needs to know of one approved live ticket: its side, the size the owner was allocated and the plan's entry and stop (the stop is null when the plan has none).</summary>
public sealed record TicketPlan(Side Side, long AllocatedQty, decimal? PlanEntry, decimal? PlanStop);
