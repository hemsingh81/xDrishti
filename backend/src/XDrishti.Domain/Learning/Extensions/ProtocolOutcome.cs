namespace XDrishti.Domain.Learning.Extensions;

/// <summary>What one protocol of P15-10 concluded: the verdict word, the named missing and failed quantities, the stated limits and the two JSON texts of the record. No JSON text holds a digit for a value that is unavailable.</summary>
/// <param name="Verdict">KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE.</param>
/// <param name="Missing">What is missing, named with its numbers, for NOT_EVALUABLE.</param>
/// <param name="Failed">What failed the keep rule, for DROP and INCONCLUSIVE.</param>
/// <param name="Limits">The stated limits.</param>
/// <param name="EstimatesJson">The estimates with their intervals.</param>
/// <param name="EvidenceJson">The evidence block (P8-00 D3).</param>
/// <param name="FoldsTotal">Seasons or folds used.</param>
/// <param name="FoldsEvaluable">Of those, the ones that could be judged.</param>
/// <param name="Rows">Trades, events or labels the protocol counted.</param>
public sealed record ProtocolOutcome(
    ExtensionVerdict Verdict,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Limits,
    string EstimatesJson,
    string EvidenceJson,
    int FoldsTotal,
    int FoldsEvaluable,
    int Rows);
