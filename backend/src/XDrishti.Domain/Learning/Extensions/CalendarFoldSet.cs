namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The calendar folds of a comparison and the number of windows dropped because the session list did not cover them (P13-05).</summary>
/// <param name="Folds">The windows in order, used or skipped.</param>
/// <param name="IncompleteDropped">Windows not used because the session list ends before their last day.</param>
public sealed record CalendarFoldSet(IReadOnlyList<CalendarFold> Folds, int IncompleteDropped);
