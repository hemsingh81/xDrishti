namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>The newest rolling quality results of chosen fetch keys with their run ids (P14-11, ADR 0199). Filtered by underlying, keys and date range in the query; read only. Implemented beside <see cref="IOptionQualityStore"/>, whose reads do not name the run.</summary>
public interface IRollingQualityRuns
{
    /// <summary>The newest result per session, key and check of the underlying's rolling key-sessions with the given fetch keys in the date range (inclusive), oldest first.</summary>
    ValueTask<IReadOnlyList<RunQualityRow>> RollingResultsForKeysAsync(Guid underlyingId, IReadOnlyCollection<string> fetchKeys, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
