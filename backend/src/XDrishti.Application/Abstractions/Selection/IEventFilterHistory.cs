namespace XDrishti.Application.Abstractions.Selection;

/// <summary>The read side of the decision history (P15-12): the newest rows of <c>filings.event_filter_decisions</c>. <see cref="IEventFilterDecisions"/> adds the insert.</summary>
public interface IEventFilterHistory
{
    /// <summary>The newest rows, newest first.</summary>
    Task<IReadOnlyList<EventFilterRow>> RowsAsync(int take, CancellationToken cancellationToken);
}
