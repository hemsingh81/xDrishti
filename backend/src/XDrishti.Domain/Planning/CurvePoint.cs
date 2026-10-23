namespace XDrishti.Domain.Planning;

/// <summary>One plan-day of a top-K curve (P7-13).</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="Date">The planned session.</param>
/// <param name="Tickets">Tickets in the top K that day.</param>
/// <param name="Triggered">Of them graded.</param>
/// <param name="DayR">Sum of R of the day.</param>
/// <param name="CumulativeR">Running sum up to and including the day.</param>
public sealed record CurvePoint(long PlanId, DateOnly Date, int Tickets, int Triggered, double DayR, double CumulativeR);
