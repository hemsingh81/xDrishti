namespace XDrishti.Application.Abstractions.SimAll;

/// <summary>
/// The read side of the simulate-everything jobs that the reports use (AU-14): progress by state, nothing else. A host without access to the job tables (the read-only assistant host) registers an empty one, so a training report there answers "no such job".
/// </summary>
public interface ITrainingJobProgress
{
    /// <summary>
    /// A job by id, or the active-else-newest one when <paramref name="id"/> is null, with the counts of its chunks by state only (the sums of time, bytes and trades are 0): an index-only count, cheap enough for every report call (AU-14). Null when there is no such job.
    /// </summary>
    ValueTask<SimAllSnapshot?> GetProgressAsync(Guid? id, CancellationToken cancellationToken);

    /// <summary>The jobs, newest first, with light counts (see <see cref="GetProgressAsync"/>); <paramref name="take"/> is 1 to 50 (AU-14).</summary>
    ValueTask<IReadOnlyList<SimAllSnapshot>> ListProgressAsync(int skip, int take, CancellationToken cancellationToken);
}
