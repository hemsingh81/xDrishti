using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>
/// The one answer to "what did the market know about this company at instant t" (P15-02, D3). Rows are read only through this port and the metadata reads; the lag (<c>Filings:LagMinutes</c>) is applied here and a reader that records a result must store the lag it used. Not
/// yet referenced by any decision code (an architecture test); P15-04/05/06 name their callers deliberately.
/// </summary>
public interface IPointInTimeFilings
{
    ValueTask<AsOfResult> AsOf(Guid instrumentId, FilingKind kind, DateTimeOffset t, CancellationToken cancellationToken);
}
