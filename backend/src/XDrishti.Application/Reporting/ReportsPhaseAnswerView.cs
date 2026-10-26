namespace XDrishti.Application.Reporting;

/// <summary>The What-works answer on the default set (spec P8-17).</summary>
/// <param name="Kind">ranked, nothing_qualifies or no_runs.</param>
/// <param name="Sentence">The answer in one sentence.</param>
/// <param name="CellsLooked">Cells looked at.</param>
/// <param name="LargestN">The most trades in any cell.</param>
/// <param name="RequiredN">The sample a cell needs.</param>
public sealed record ReportsPhaseAnswerView(string Kind, string Sentence, int CellsLooked, int LargestN, int RequiredN);
