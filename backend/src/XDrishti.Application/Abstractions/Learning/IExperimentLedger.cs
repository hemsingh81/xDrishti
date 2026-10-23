namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// The experiment ledger (P6-11, ADR 0072): <c>research.experiments</c> behind a port. Insert-only. Its count over the last 30 days is the number of trials that widens the required improvement (doc 09 §12 rule 5).
/// </summary>
public interface IExperimentLedger
{
    /// <summary>How many experiments other than <paramref name="excludingKey"/> were recorded at or after <paramref name="since"/>: the trials already looked at.</summary>
    ValueTask<int> CountSinceAsync(DateTimeOffset since, string excludingKey, CancellationToken cancellationToken);

    /// <summary>The experiment with this key, in full, or null.</summary>
    ValueTask<StoredExperiment?> FindByKeyAsync(string experimentKey, CancellationToken cancellationToken);

    /// <summary>
    /// Records the experiment under one global lock, so two experiments never get the same trial number. Inside the lock the key is checked (stored already: nothing is written and that row's id is returned, not created), and the trials are recounted
    /// (experiments since <paramref name="since"/> other than this key, plus one, at least <paramref name="askedTrials"/>); when that differs from <c>experiment.Trials</c> nothing is written and <see cref="RecordedExperiment.RequiredTrials"/> says what to judge with.
    /// </summary>
    ValueTask<RecordedExperiment> AppendAsync(ExperimentToStore experiment, int askedTrials, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>One experiment in full, or null.</summary>
    ValueTask<StoredExperiment?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The newest experiments first, without report text or parameters.</summary>
    ValueTask<IReadOnlyList<StoredExperiment>> ListAsync(int limit, CancellationToken cancellationToken);
}
