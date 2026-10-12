using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Abstractions.Market.Quality;

/// <summary>The records of quality runs and their results (<c>market.quality_runs</c>, <c>market.quality_results</c>): written by the quality phase, never deleted.</summary>
public interface IQualityRuns
{
    /// <summary>
    /// Records the run as the one in progress. Runs still marked running whose data job is no longer running were interrupted and are marked failed first. False when another run is in progress
    /// (the database allows one), in which case nothing is recorded.
    /// </summary>
    ValueTask<bool> TryStartAsync(QualityRun run, CancellationToken cancellationToken);

    /// <summary>Writes the progress and the end of the run (status, counts, score, error). Only a run still marked running is written: one failed meanwhile (recovery) keeps that verdict.</summary>
    ValueTask PublishAsync(QualityRun run, CancellationToken cancellationToken);

    /// <summary>Stores the results of instruments (one row per run and instrument; a repeat replaces it), in one transaction.</summary>
    ValueTask SaveResultsAsync(IReadOnlyCollection<QualityResult> results, CancellationToken cancellationToken);

    /// <summary>Marks the runs still running that started before <paramref name="processStartedAt"/> as failed (interrupted); returns how many.</summary>
    ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken);
}
