using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>Reads stored shareholding groups (P15-04): those of one instrument whose time is at or before the cutoff (the caller passes <c>t - lag</c>). A group without a time is never returned.</summary>
public interface IShareholdingRows
{
    ValueTask<IReadOnlyList<ShareholdingEnvelope>> GroupsAsync(Guid instrumentId, DateTimeOffset cutoff, CancellationToken cancellationToken);
}
