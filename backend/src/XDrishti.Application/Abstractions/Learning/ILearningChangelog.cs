namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where the learning changelog is kept (spec P6-08, ADR 0073): <c>research.learning_changelog</c> behind a port. Insert-only; a revert is a new row.</summary>
public interface ILearningChangelog
{
    /// <summary>
    /// Appends a row under a lock on its subject. Nothing is written when the subject's latest row does not end in <see cref="NewChangelogEntry.FromState"/> (a cell with no rows is in <c>research</c>), or, for a revert, when the reverted row is not the
    /// subject's latest or is not a transition.
    /// </summary>
    ValueTask<ChangelogAppend> AppendAsync(NewChangelogEntry entry, CancellationToken cancellationToken);

    /// <summary>One row, or null.</summary>
    ValueTask<ChangelogEntry?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The newest rows first, optionally of one subject kind (<c>cell</c> or <c>model</c>) and one subject.</summary>
    ValueTask<IReadOnlyList<ChangelogEntry>> ListAsync(string? subjectKind, string? subject, int limit, CancellationToken cancellationToken);

    /// <summary>Every row of a subject, oldest first: what a replay reads.</summary>
    ValueTask<IReadOnlyList<ChangelogEntry>> HistoryAsync(string subject, CancellationToken cancellationToken);
}
