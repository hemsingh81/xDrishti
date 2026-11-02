using System.Text.Json;

namespace XDrishti.Application.Learning;

/// <summary>
/// One stored evaluation as the API shows it (P15-10): the verdict with the named missing quantities, the counts, the stated limits and the three JSON blocks as JSON (estimates with their intervals, the evidence block, the inputs). A value that is unavailable is the text
/// <c>unavailable</c> inside those blocks, never a number.
/// </summary>
public sealed record ExtensionEvaluationView(
    string Id,
    string Family,
    string Protocol,
    string? ExtensionVersion,
    string VersionKey,
    string BaseVersion,
    string Verdict,
    int Trials,
    double Factor,
    string FoldUnit,
    int FoldsTotal,
    int FoldsEvaluable,
    int Rows,
    int? Flips,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Limits,
    JsonElement Estimates,
    JsonElement Evidence,
    JsonElement Inputs,
    bool HoldOutRead);
