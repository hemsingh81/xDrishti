using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The result facts of an instrument as known at an instant (P15-03, ADR 0184): a port with no decision caller yet. A reader at <c>t</c> sees only facts of filings visible at <c>t</c>.</summary>
public interface IResultFactsAsOf
{
    ValueTask<ResultFactsAnswer> Facts(Guid instrumentId, DateTimeOffset t, CancellationToken cancellationToken);
}
