namespace XDrishti.Domain.Paper;

/// <summary>A small table of a review section (the closed trades of the week), as text cells.</summary>
/// <param name="Columns">The headers.</param>
/// <param name="Rows">The rows; each has one cell per column.</param>
public sealed record ReviewTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
