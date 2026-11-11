namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>The NSE lot of one underlying (a master share or index instrument) in the nearest expiry column.</summary>
public sealed record NseLot(Guid UnderlyingId, int LotSize);

/// <summary>What to store for a fetched lot file: the lots, the India date of the fetch, the file URL (the provenance), the source state, and how the fetch record reads once the number of lot rows really written is known.</summary>
public sealed record NseLotApplication(IReadOnlyList<NseLot> Lots, DateOnly Today, string SourceUrl, SourceUpdate Source, Func<int, FetchRecord> Fetch);

/// <summary>
/// Stores the NSE lots of one fetch (BK-05b) as dated, unconfirmed, insert-only <c>nse_mktlots</c> rows, only where the lot differs from the newest <c>nse_mktlots</c> row of that underlying, and the fetch row with the source state, in ONE transaction.
/// Returns the number of lot rows written; null when nothing was written (the transaction failed and rolled back). No lot row is ever updated or deleted and the master's lot is not touched.
/// </summary>
public interface INseLotApplier
{
    ValueTask<int?> ApplyAsync(NseLotApplication application, CancellationToken cancellationToken);
}
