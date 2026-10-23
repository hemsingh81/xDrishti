namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The result of an append: the row id (for an opening, the lesson id, also when it already existed) and the status.</summary>
/// <param name="Id">The row or lesson id; 0 when stale.</param>
/// <param name="Status">What happened.</param>
public sealed record LessonAppend(long Id, LessonAppendStatus Status);
