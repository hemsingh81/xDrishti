namespace XDrishti.Application.Learning;

/// <summary>One finding of a scan.</summary>
/// <param name="Monitor">The monitor code.</param>
/// <param name="Subject">The cell or model.</param>
/// <param name="Problem">The problem with its numbers.</param>
/// <param name="Status"><c>opened</c>, <c>existing</c> (already a lesson) or <c>found</c> (dry run, not written).</param>
/// <param name="LessonId">The lesson id; 0 for a dry run of a new finding.</param>
public sealed record LessonScanItem(string Monitor, string Subject, string Problem, string Status, long LessonId);
