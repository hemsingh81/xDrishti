namespace XDrishti.Domain.Planning;

/// <summary>One ISO week of graded plan tickets for the Journey (spec P8-12).</summary>
/// <param name="IsoWeek">The week, <c>2026-W43</c>.</param>
/// <param name="Start">The Monday.</param>
/// <param name="Graded">Tickets that were triggered and graded (they have an R).</param>
/// <param name="TotalR">Sum of their R.</param>
/// <param name="MeanR">Mean R; null when the week is <c>insufficient</c>.</param>
/// <param name="State"><c>ok</c>, or <c>insufficient</c> when fewer than <see cref="JourneyWeeks.MinGraded"/> tickets were graded (n is shown, no mean).</param>
public sealed record JourneyWeek(string IsoWeek, DateOnly Start, int Graded, double TotalR, double? MeanR, string State);
