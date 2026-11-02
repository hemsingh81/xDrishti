using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// The results event instants of instruments, for the <c>news_results</c> loss tag (P15-06, ADR 0182): the broadcast times of their stored results filings, and the 09:15 India time of a calendar meeting that has no filing. An instrument with neither is not in the answer (unavailable,
/// not "no news"). A read that fails answers null (events not supplied). Describes the past; never a decision-time input.
/// </summary>
public interface ILossTagEvents
{
    ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<EventInstant>>?> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);
}
