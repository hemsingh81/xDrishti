using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>
/// The read side of the option archive for the quality checks (P14-04), the only reader of <c>options.chain_snapshots</c>, <c>options.chain_requests</c>, <c>options.chain_sessions</c>, <c>options.rolling_bars</c> and <c>options.fetch_log</c> besides the
/// recorders' own resume reads. Every read is bounded by a time range and an underlying (or a limit); none writes.
/// </summary>
public interface IOptionArchiveReader
{
    /// <summary>The India days up to <paramref name="upTo"/> on which a chain request was made (a crashed day has requests and an end row of zero) that no chain result exists for yet, oldest first, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<DateOnly>> PendingChainSessionsAsync(DateOnly upTo, int limit, CancellationToken cancellationToken);

    /// <summary>Per underlying, the requests of the India day.</summary>
    ValueTask<IReadOnlyList<ChainRequestSummary>> ChainRequestSummariesAsync(DateOnly session, CancellationToken cancellationToken);

    /// <summary>The snapshot rows of one underlying on one India day.</summary>
    ValueTask<IReadOnlyList<ChainPoint>> ChainPointsAsync(Guid underlyingId, DateOnly session, CancellationToken cancellationToken);

    /// <summary>The India days with at least one chain request from <paramref name="from"/> to <paramref name="to"/> (inclusive).</summary>
    ValueTask<IReadOnlySet<DateOnly>> ChainSessionDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The dates in the range that already have a chain result.</summary>
    ValueTask<IReadOnlySet<DateOnly>> ChainResultDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The first India day with a chain request; null when none is recorded.</summary>
    ValueTask<DateOnly?> FirstChainSessionAsync(CancellationToken cancellationToken);

    /// <summary>The underlyings that had a request since <paramref name="since"/>.</summary>
    ValueTask<IReadOnlyList<Guid>> RecentChainUnderlyingsAsync(DateOnly since, CancellationToken cancellationToken);

    /// <summary>Finished fetch windows that ended on or before <paramref name="upTo"/> and have no rolling result yet, oldest first, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<RollingWindow>> PendingRollingWindowsAsync(DateOnly upTo, int limit, CancellationToken cancellationToken);

    /// <summary>The stored bars of a window with the underlying's 1-minute close at each bar's end minute.</summary>
    ValueTask<IReadOnlyList<RollingPoint>> RollingPointsAsync(RollingWindow window, CancellationToken cancellationToken);

    /// <summary>The India days in [<paramref name="from"/>, <paramref name="to"/>) on which any series of the underlying has a stored bar (read once per underlying and window); empty when it has none.</summary>
    ValueTask<IReadOnlySet<DateOnly>> ActiveDaysAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
