namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One trade of an unfiltered stored run, classified at its decision instant (P15-10, protocol F).</summary>
/// <param name="Key">The trade key.</param>
/// <param name="Date">The India date of the entry; its calendar quarter is the season.</param>
/// <param name="R">The gross result in R.</param>
/// <param name="Fate">Skipped, taken or unavailable.</param>
/// <param name="Reason">For unavailable: <c>no_entry</c>, <c>only_cancelled</c> or <c>no_upcoming_entry</c>; any other text counts as <c>other</c>.</param>
public sealed record FilterTrade(string Key, DateOnly Date, double R, FilterFate Fate, string? Reason = null);
