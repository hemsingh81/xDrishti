using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>One ticket with its grade as the API shows it.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="Rank">Rank within the plan.</param>
/// <param name="Group">primary, reserve or rejected.</param>
/// <param name="Symbol">The instrument.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="State">The newest ticket state.</param>
/// <param name="Decision">approved, modified, rejected, lapsed or undecided; empty for a rejected candidate.</param>
/// <param name="Status">graded, not_triggered, not_gradable or pending (no grade stored yet).</param>
/// <param name="R">Result in R.</param>
/// <param name="MaeR">Adverse excursion in R.</param>
/// <param name="MfeR">Favourable excursion in R.</param>
/// <param name="ExitReason">Why the simulated trade ended.</param>
/// <param name="CostBasis">What R is net of.</param>
/// <param name="Detail">Why, in words.</param>
public sealed record TicketGradeView(long ItemId, int Rank, string Group, string Symbol, string Side, string State, string Decision, string Status, double? R, double? MaeR, double? MfeR, string? ExitReason, string? CostBasis, string? Detail);

/// <summary>The grades of one plan (P7-13).</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="Basis">Always <c>simulated on plan prices</c>.</param>
/// <param name="Tickets">Every ticket of the plan.</param>
/// <param name="Pending">Tickets without a stored grade.</param>
/// <param name="Report">The curves and groups of this plan.</param>
public sealed record PlanGradeView(long PlanId, DateOnly ForDate, string Basis, IReadOnlyList<TicketGradeView> Tickets, int Pending, GradeReportData Report);
