namespace XDrishti.Domain.Reporting;

/// <summary>What the stored answer to "what works" is on the default set (spec P8-17).</summary>
/// <param name="Kind">ranked, nothing_qualifies or no_runs.</param>
/// <param name="Sentence">The answer in one sentence, with the numbers the screen shows.</param>
/// <param name="CellsLooked">Cells looked at.</param>
/// <param name="LargestN">The most trades in any cell.</param>
/// <param name="RequiredN">The sample a cell needs to be ranked.</param>
public sealed record ReportsPhaseAnswer(string Kind, string Sentence, int CellsLooked, int LargestN, int RequiredN)
{
    /// <summary>No stored run: nothing to rank.</summary>
    public static ReportsPhaseAnswer NoRuns { get; } = new("no_runs", "No stored run: nothing to rank.", 0, 0, 0);
}
