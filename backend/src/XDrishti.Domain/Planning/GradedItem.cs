namespace XDrishti.Domain.Planning;

/// <summary>One graded ticket as the report reads it (P7-13).</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="Date">The planned session.</param>
/// <param name="Rank">The rank within the plan.</param>
/// <param name="Group">Primary, reserve or rejected.</param>
/// <param name="State">The newest ticket state (Proposed for a rejected candidate, which has none).</param>
/// <param name="Grade">The grade.</param>
public sealed record GradedItem(long PlanId, DateOnly Date, int Rank, TicketGroup Group, TicketState State, TicketGrade Grade);
