using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The stored <c>results</c> filings with a known broadcast time (state ok) of the given instruments: envelope facts only (instrument, period, time), never a file (P15-06, the loss tag). SELECT only.</summary>
public interface IResultsFilingTimes
{
    ValueTask<IReadOnlyList<ResultsFilingTime>> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);
}
