namespace XDrishti.Domain.Reporting;

/// <summary>The one rule of plan versus actual: how many graded tickets a figure needs (P8-04).</summary>
/// <param name="MinTickets">Fewest graded tickets for a number; fewer is <c>insufficient</c>.</param>
public sealed record PlanVsActualOptions(int MinTickets = 15);
