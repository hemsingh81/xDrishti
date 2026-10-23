namespace XDrishti.Domain.Planning;

/// <summary>The figures of a set of graded tickets (P7-13). R is summed over the graded (triggered) tickets only; the others add nothing and are counted.</summary>
/// <param name="Name">What the set is (<c>top-3</c>, <c>primary</c>, <c>approved</c> ...).</param>
/// <param name="Tickets">All tickets in the set.</param>
/// <param name="Triggered">Graded tickets (with an R).</param>
/// <param name="NotTriggered">Tickets with no simulated trade.</param>
/// <param name="NotGradable">Tickets that cannot be replayed.</param>
/// <param name="TotalR">Sum of R.</param>
/// <param name="MeanR">Mean R per triggered ticket; null with none.</param>
/// <param name="WinRate">Share of triggered tickets with R above zero; null with none.</param>
/// <param name="Note">The honest note on the sample size; null when the sample reaches <see cref="GradeReport.IndicativeBelow"/>.</param>
public sealed record GradeStats(string Name, int Tickets, int Triggered, int NotTriggered, int NotGradable, double TotalR, double? MeanR, double? WinRate, string? Note);
