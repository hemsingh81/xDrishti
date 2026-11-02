using XDrishti.Domain.Selection;

namespace XDrishti.Application.Abstractions.Selection;

/// <summary>
/// The effective event-filter switch (P15-06, ADR 0182): the newest row of <c>filings.event_filter_decisions</c>; no row is OFF and a table that cannot be read is OFF (the live paths then change nothing). It never throws except for cancellation.
/// </summary>
public interface IEventFilterSwitch
{
    ValueTask<EventFilterState> CurrentAsync(CancellationToken cancellationToken);
}
