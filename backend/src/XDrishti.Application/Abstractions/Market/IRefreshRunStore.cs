using XDrishti.Domain.Market;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>Records of master-list refreshes. At most one run is <c>running</c> at any time, across all instances.</summary>
public interface IRefreshRunStore
{
    /// <summary>
    /// Registers <paramref name="run"/> as the running refresh, unless another one is running (a run older than <paramref name="staleAfter"/> counts as
    /// interrupted and is marked failed). False when another refresh is running. Serialised by a database advisory lock so two instances never overlap.
    /// </summary>
    ValueTask<bool> TryStartAsync(RefreshRun run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The run to work on (tracked: changes are written by <see cref="SaveAsync"/>); null when it does not exist.</summary>
    ValueTask<RefreshRun?> GetAsync(Guid id, CancellationToken cancellationToken);

    ValueTask SaveAsync(CancellationToken cancellationToken);

    /// <summary>Writes the phase and row count of a running refresh at once (visible to status requests while the run is busy).</summary>
    ValueTask ReportProgressAsync(Guid id, RefreshPhase phase, long rowsRead, CancellationToken cancellationToken);

    /// <summary>The refresh in progress, if any.</summary>
    ValueTask<RefreshRun?> GetRunningAsync(CancellationToken cancellationToken);

    /// <summary>The latest finished refresh, if any.</summary>
    ValueTask<RefreshRun?> GetLastFinishedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The validators the last successful or unchanged refresh ended with: (Dhan, sector list). Null when there is nothing to rely on: no such
    /// refresh, it worked under other rules than <paramref name="rulesVersion"/>, or its sector list could not be applied.
    /// </summary>
    ValueTask<(SourceStamp Dhan, SourceStamp Sector)?> GetLastStampsAsync(int rulesVersion, CancellationToken cancellationToken);

    /// <summary>Marks every refresh still <c>running</c> that started before <paramref name="processStartedAt"/> as failed (interrupted); returns how many.</summary>
    ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<RefreshRun>> ListAsync(int take, CancellationToken cancellationToken);
}
