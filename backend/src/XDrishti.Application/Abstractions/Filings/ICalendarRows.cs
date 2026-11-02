using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>
/// Reads stored board-meeting rows of purpose <c>results</c> (P15-04). The query only narrows by instrument, the kind and the cutoff <c>broadcast_ts &lt;= cutoff</c> (the caller passes <c>t - lag</c>); <c>ResultsDays</c> decides. A row without an intimation time is never returned.
/// </summary>
public interface ICalendarRows
{
    /// <summary>The rows of these instruments (null = every instrument) intimated at or before <paramref name="cutoff"/>.</summary>
    ValueTask<IReadOnlyList<CalendarRow>> ResultsRowsAsync(IReadOnlyCollection<Guid>? instrumentIds, DateTimeOffset cutoff, CancellationToken cancellationToken);
}
