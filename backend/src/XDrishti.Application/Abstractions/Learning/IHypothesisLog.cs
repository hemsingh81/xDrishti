namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// The hypothesis log (spec P6-10, ADR 0076): <c>research.hypotheses</c> behind a port. Insert-only; a state change is a new row that repeats the facts of the hypothesis, so the newest row of a hypothesis is its current view.
/// </summary>
public interface IHypothesisLog
{
    /// <summary>Writes the proposing row unless the open hypotheses are already <paramref name="maxOpen"/> (counted under one global lock).</summary>
    ValueTask<HypothesisAppend> ProposeAsync(NewHypothesisRow row, int maxOpen, CancellationToken cancellationToken);

    /// <summary>Writes a state-change row under a lock per hypothesis, after checking again that the newest row is still in <c>row.FromState</c> (else <see cref="HypothesisAppendStatus.Stale"/>).</summary>
    ValueTask<HypothesisAppend> AppendAsync(NewHypothesisRow row, CancellationToken cancellationToken);

    /// <summary>The newest row of a hypothesis, or null.</summary>
    ValueTask<HypothesisRow?> FindAsync(long hypothesisId, CancellationToken cancellationToken);

    /// <summary>Every row of a hypothesis, oldest first.</summary>
    ValueTask<IReadOnlyList<HypothesisRow>> HistoryAsync(long hypothesisId, CancellationToken cancellationToken);

    /// <summary>The newest row of each hypothesis, newest hypothesis first, optionally only those in a state.</summary>
    ValueTask<IReadOnlyList<HypothesisRow>> ListAsync(string? state, int limit, CancellationToken cancellationToken);

    /// <summary>How many hypotheses are open (proposed, approved, running or judged).</summary>
    ValueTask<int> CountOpenAsync(CancellationToken cancellationToken);
}
