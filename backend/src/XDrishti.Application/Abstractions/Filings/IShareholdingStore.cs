using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>A shareholding group ready to be stored: its envelope (kind <c>shareholding</c>, the group's time as the broadcast time) and its category lines.</summary>
public sealed record NewShareholding(NewFiling Filing, IReadOnlyList<ShareholdingLine> Lines);

/// <summary><c>filings.filings</c> plus <c>filings.shareholding</c> (P15-04): one group is one transaction, insert-only, the group rule of the envelope table applied under its lock. Idempotent on the content hash.</summary>
public interface IShareholdingStore
{
    ValueTask<FilingInsertResult> InsertAsync(NewShareholding group, CancellationToken cancellationToken);
}
