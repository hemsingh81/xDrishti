using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>
/// The results-calendar snapshot of a research run (P15-06, ADR 0182): one read of the stored results rows of the given instruments that can be visible to a decision on or before <c>through</c> (intimation + lag at or before the end of that India date), made before the walk, so the walk
/// does no I/O. A failed read is a failure result, never an exception into the run and never an empty snapshot that looks like "no events".
/// </summary>
public interface IEventCalendarSnapshots
{
    ValueTask<Result<EventCalendarSnapshot>> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly through, CancellationToken cancellationToken);
}
