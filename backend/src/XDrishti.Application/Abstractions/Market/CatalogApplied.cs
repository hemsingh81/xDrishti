namespace XDrishti.Application.Abstractions.Market;

/// <summary>What applying a refresh did to the master list.</summary>
public sealed record CatalogApplied(int Added, int Updated, int Retired, int Revived, int Unchanged, int TotalActive);
