using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>What merging bars of the CSV source into the stored ones would do, in bars: the same classes as <see cref="BarMergeResult"/>.</summary>
public sealed record BarComparisonResult(long Inserted, long Updated, long Unchanged, long KeptDhan);

/// <summary>
/// The dry run of a CSV merge (ADR 0017 §6): classifies bars against the stored ones of one instrument with the precedence of <see cref="IBarStore"/> (a CSV bar never replaces a Dhan bar,
/// replaces a CSV bar only when the values differ), without writing anything.
/// </summary>
public interface IBarComparison
{
    /// <summary>Looks up the stored bars of the instrument at the minutes of <paramref name="bars"/> (primary key lookups: the cost is the number of bars given) and counts what the merge would do. The bars must have no minute twice.</summary>
    ValueTask<BarComparisonResult> CompareAsync(Guid instrumentId, ReadOnlyMemory<Bar> bars, CancellationToken cancellationToken);
}
