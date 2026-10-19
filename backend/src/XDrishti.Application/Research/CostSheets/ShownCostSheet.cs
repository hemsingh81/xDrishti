namespace XDrishti.Application.Research.CostSheets;

/// <summary>A stored sheet as <c>cost-sheet show</c> prints it.</summary>
/// <param name="Id">The sheet id.</param>
/// <param name="CanonicalJson">The canonical text, <c>checked</c> included.</param>
/// <param name="RatesSha256">The hash of the rates.</param>
/// <param name="Label">The run label of the sheet (<c>costs: sheet …</c> with its notes).</param>
public sealed record ShownCostSheet(string Id, string CanonicalJson, string RatesSha256, string Label);
