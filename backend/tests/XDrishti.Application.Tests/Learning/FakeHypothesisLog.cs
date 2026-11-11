using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The hypothesis log in memory: a state change repeats the row of the hypothesis, the newest row of an id is its current view, the open limit is counted over the newest rows.</summary>
internal sealed class FakeHypothesisLog : IHypothesisLog
{
    public List<HypothesisRow> Rows { get; } = [];

    private bool IsOpen(long id) => HypothesisLifecycle.IsOpen(HypothesisStateText.TryParse(Latest(id).Row.State)!.Value);

    private HypothesisRow Latest(long id) => Rows.Last(r => r.HypothesisId == id);

    public ValueTask<HypothesisAppend> ProposeAsync(NewHypothesisRow row, int maxOpen, CancellationToken cancellationToken)
    {
        if (Rows.Select(r => r.HypothesisId).Distinct().Count(IsOpen) >= maxOpen)
        {
            return ValueTask.FromResult(new HypothesisAppend(0, HypothesisAppendStatus.OpenLimit));
        }

        var id = Rows.Count + 1L;
        Rows.Add(new HypothesisRow(id, row));
        return ValueTask.FromResult(new HypothesisAppend(id, HypothesisAppendStatus.Created));
    }

    public ValueTask<HypothesisAppend> AppendAsync(NewHypothesisRow row, CancellationToken cancellationToken)
    {
        if (Latest(row.HypothesisId!.Value).Row.State != row.FromState)
        {
            return ValueTask.FromResult(new HypothesisAppend(0, HypothesisAppendStatus.Stale));
        }

        var id = Rows.Count + 1L;
        Rows.Add(new HypothesisRow(id, row));
        return ValueTask.FromResult(new HypothesisAppend(id, HypothesisAppendStatus.Created));
    }

    public ValueTask<HypothesisRow?> FindAsync(long hypothesisId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Rows.Any(r => r.HypothesisId == hypothesisId) ? Latest(hypothesisId) : null);

    public ValueTask<IReadOnlyList<HypothesisRow>> HistoryAsync(long hypothesisId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<HypothesisRow>>([.. Rows.Where(r => r.HypothesisId == hypothesisId)]);

    public ValueTask<IReadOnlyList<HypothesisRow>> ListAsync(string? state, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<HypothesisRow>>([.. Rows.Select(r => r.HypothesisId).Distinct().Select(Latest).Where(r => state is null || r.Row.State == state).Take(limit)]);

    public ValueTask<int> CountOpenAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Rows.Select(r => r.HypothesisId).Distinct().Count(IsOpen));
}
