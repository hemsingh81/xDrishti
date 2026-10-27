namespace XDrishti.Application.Abstractions.Ai;

/// <summary>One line of <c>list_tables</c>. <paramref name="Classes"/> is <c>["C1"]</c> or <c>["C2"]</c>; a C2 view while the portfolio context is off has <paramref name="ApproxRows"/> 0 and a note.</summary>
public sealed record CatalogView(string Name, string Description, string Grain, IReadOnlyList<string> Classes, long ApproxRows, string? Note);
