namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>The quality runs and results (<c>options.quality_runs</c>, <c>options.quality_results</c>), insert-only: a re-run writes a new run, readers take the newest result per session and check.</summary>
public interface IOptionQualityStore
{
    /// <summary>Reserves the id of a run from the sequence; no row is written until <see cref="FinishRunAsync"/>.</summary>
    ValueTask<long> StartRunAsync(CancellationToken cancellationToken);

    ValueTask AddResultsAsync(long runId, IReadOnlyList<OptionQualityRow> rows, CancellationToken cancellationToken);

    ValueTask FinishRunAsync(long runId, string trigger, DateTimeOffset startedAt, DateTimeOffset endedAt, int sessions, int results, string? note, CancellationToken cancellationToken);

    /// <summary>The newest chain results of an underlying up to a date (all sessions, for the cumulative count).</summary>
    ValueTask<IReadOnlyList<OptionQualityRow>> ChainResultsAsync(Guid underlyingId, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The newest rolling results of an underlying in a date range, covering at most <paramref name="limit"/> key-sessions (a day of one fetch key is one key-session, whatever its number of checks).</summary>
    ValueTask<IReadOnlyList<OptionQualityRow>> RollingResultsAsync(Guid underlyingId, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken);
}
