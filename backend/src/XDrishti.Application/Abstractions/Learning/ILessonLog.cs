namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// The lesson log (spec P6-16, ADR 0078): <c>research.lessons</c> behind a port. Insert-only; a state change is a new row that repeats the facts of the lesson, so the newest row is its current view.
/// </summary>
public interface ILessonLog
{
    /// <summary>Writes the opening row unless a lesson with the same monitor, subject and evidence key exists (then <see cref="LessonAppendStatus.Existing"/> and that lesson's id).</summary>
    ValueTask<LessonAppend> OpenAsync(NewLessonRow row, CancellationToken cancellationToken);

    /// <summary>Writes a state-change row under a lock per lesson after checking again that the newest row is still in <c>row.FromState</c> (else <see cref="LessonAppendStatus.Stale"/>).</summary>
    ValueTask<LessonAppend> AppendAsync(NewLessonRow row, CancellationToken cancellationToken);

    /// <summary>The id of the lesson recorded for this finding, or null.</summary>
    ValueTask<long?> FindIdByEvidenceAsync(string monitorKind, string subject, string evidenceKey, CancellationToken cancellationToken);

    /// <summary>The newest row of a lesson, or null.</summary>
    ValueTask<LessonRow?> FindAsync(long lessonId, CancellationToken cancellationToken);

    /// <summary>Every row of a lesson, oldest first.</summary>
    ValueTask<IReadOnlyList<LessonRow>> HistoryAsync(long lessonId, CancellationToken cancellationToken);

    /// <summary>The newest row of each lesson, newest lesson first, optionally only those in a state.</summary>
    ValueTask<IReadOnlyList<LessonRow>> ListAsync(string? state, int limit, CancellationToken cancellationToken);
}
