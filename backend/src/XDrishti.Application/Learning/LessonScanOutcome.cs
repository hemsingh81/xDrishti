namespace XDrishti.Application.Learning;

/// <summary>The findings of a scan and what became of each.</summary>
/// <param name="Items">One per finding, in a fixed order.</param>
/// <param name="NotBuilt">The monitors that did not run, and why.</param>
public sealed record LessonScanOutcome(IReadOnlyList<LessonScanItem> Items, IReadOnlyList<string> NotBuilt);
