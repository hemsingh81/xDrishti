namespace XDrishti.Application.Research.CostSheets;

/// <summary>What <see cref="LoadCostSheetCommand"/> did.</summary>
/// <param name="Id">The sheet id.</param>
/// <param name="RatesSha256">The hash of the rates now stored.</param>
/// <param name="Label">The sheet's label.</param>
/// <param name="IsChecked">Whether the sheet carries a <c>checked</c> note.</param>
/// <param name="Status">What happened to the row.</param>
/// <param name="PreviousRatesSha256">The hash of the rates that were stored before, when a row existed and its text parsed.</param>
public sealed record LoadedCostSheet(string Id, string RatesSha256, string Label, bool IsChecked, LoadStatus Status, string? PreviousRatesSha256);
