using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The shareholding of an instrument as known at instant t (P15-04): the current visible group and the previous quarter's, or <c>unavailable</c>. Not yet referenced by any decision code (an architecture test).</summary>
public interface IShareholdingAsOf
{
    ValueTask<ShareholdingAnswer> Latest(Guid instrumentId, DateTimeOffset t, CancellationToken cancellationToken);
}
