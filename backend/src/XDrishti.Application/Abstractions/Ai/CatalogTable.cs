namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The answer of <c>describe_table</c>.</summary>
public sealed record CatalogTable(string Name, string Description, string Grain, IReadOnlyList<string> Classes, IReadOnlyList<CatalogColumn> Columns, IReadOnlyList<string> Limits);
