namespace XDrishti.Domain.Learning;

/// <summary>A problem a monitor found on replayed history (spec P6-16): what, where, the key that makes it the same finding next time, and the numbers.</summary>
/// <param name="Monitor">The monitor.</param>
/// <param name="Subject">The cell key, or <c>model:ID</c>.</param>
/// <param name="EvidenceKey">Stable text identifying this finding; the same finding gives the same key.</param>
/// <param name="Problem">One sentence, with the numbers.</param>
public sealed record LessonFinding(LessonMonitorKind Monitor, string Subject, string EvidenceKey, string Problem);
