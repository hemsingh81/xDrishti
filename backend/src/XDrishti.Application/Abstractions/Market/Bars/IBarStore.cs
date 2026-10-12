using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>What merging a set of bars did, in bars (minutes). <c>KeptDhan</c> are CSV bars that were not stored because a Dhan bar is there already.</summary>
public sealed record BarMergeResult(long Inserted, long Updated, long Unchanged, long KeptDhan);

/// <summary>
/// The file a merge came from, recorded with it (and the merge's counts): <see cref="ICsvFileLog"/> then lists it. <paramref name="FirstBar"/> and <paramref name="LastBar"/> are the first and last bar
/// start of the file's accepted rows: the range an undo of the load works on.
/// </summary>
public sealed record CsvFileMark(string Name, long Size, DateTimeOffset ModifiedAt, long Rows, long Repaired, long Rejected, DateTimeOffset? FirstBar = null, DateTimeOffset? LastBar = null);

/// <summary>
/// The stored 1-minute bars. Prices are raw. Merging follows the precedence of ADR 0016 §3: a Dhan bar replaces a CSV or Dhan bar of the same minute, a CSV
/// bar replaces only a CSV bar; same values are left alone.
/// </summary>
public interface IBarStore
{
    /// <summary>
    /// Starts merging bars of one instrument and source: the bars are added in batches (streamed into a staging table, never held in memory) and merged,
    /// together with the instrument's coverage and the range to aggregate again, in one transaction by <see cref="IBarMergeSession.CompleteAsync"/>.
    /// Disposing the session without completing it discards everything.
    /// </summary>
    ValueTask<IBarMergeSession> BeginMergeAsync(Guid instrumentId, BarSource source, CancellationToken cancellationToken);

    /// <summary>The stored bars of the instrument from <paramref name="from"/> up to and including <paramref name="to"/>, oldest first.</summary>
    ValueTask<IReadOnlyList<Bar>> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>One merge in progress; see <see cref="IBarStore.BeginMergeAsync"/>.</summary>
public interface IBarMergeSession : IAsyncDisposable
{
    /// <summary>Adds bars to the merge. The bars must be valid (<see cref="BarRules"/>) and have no minute twice, in this and in all earlier batches. The memory is read before the call returns.</summary>
    ValueTask AddAsync(ReadOnlyMemory<Bar> bars, CancellationToken cancellationToken);

    /// <summary>Merges what was added, records <paramref name="file"/> (if any) and commits.</summary>
    ValueTask<BarMergeResult> CompleteAsync(CsvFileMark? file, CancellationToken cancellationToken);
}
