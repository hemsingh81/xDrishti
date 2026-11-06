namespace XDrishti.Domain.Readiness;

/// <summary>One row of the 15-row edge checklist (spec CF-11 D1): what it asks, the threshold, the figure it was judged on, the producer it reads and the result.</summary>
/// <param name="Number">1 to 15.</param>
/// <param name="Name">The criterion.</param>
/// <param name="Producer">The slice or source that produces the input (a row whose producer does not exist yet reads unavailable and says which).</param>
/// <param name="Status">Pass, fail or unavailable.</param>
/// <param name="Threshold">The go threshold in words and numbers.</param>
/// <param name="Value">The figure the row was judged on, or the reason it could not be.</param>
public sealed record ChecklistRow(int Number, string Name, string Producer, RowStatus Status, string Threshold, string Value);
