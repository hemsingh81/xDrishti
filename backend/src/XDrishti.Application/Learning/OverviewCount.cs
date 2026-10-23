namespace XDrishti.Application.Learning;

/// <summary>A name and how many records carry it (P6-18).</summary>
/// <param name="Name">A role, outcome or state.</param>
/// <param name="Count">How many of the newest records have it.</param>
public sealed record OverviewCount(string Name, int Count);
