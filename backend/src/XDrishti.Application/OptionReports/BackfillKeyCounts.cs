using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Application.OptionReports;

/// <summary>The backfill keys of one underlying by their latest state, counted as the backfill's own progress counts them: <c>Done</c> is every finished key (empty and no-data answers included, <c>Empty</c> and <c>NoData</c> are subsets of it); <c>GivenUp</c> by <see cref="KeyState.IsGivenUp"/>.</summary>
internal sealed record BackfillKeyCounts(Guid UnderlyingId, int Done, int Empty, int NoData, int GivenUp)
{
    public static IReadOnlyDictionary<Guid, BackfillKeyCounts> From(IReadOnlyDictionary<BackfillKey, KeyState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        return states.GroupBy(s => s.Key.UnderlyingId).ToDictionary(
            g => g.Key,
            g => new BackfillKeyCounts(
                g.Key,
                g.Count(s => s.Value.IsDone),
                g.Count(s => s.Value.DoneOutcome == BackfillOutcomes.Empty),
                g.Count(s => s.Value.DoneOutcome == BackfillOutcomes.NoData),
                g.Count(s => s.Value.IsGivenUp)));
    }
}
