using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where decision-time features are kept (P6-04, ADR 0066): <c>research.trade_features</c> behind a port. Insert-only; a row is keyed by (run key, trade key, catalog version) and a second write of it is a no-op.</summary>
public interface ITradeFeatureStore
{
    /// <summary>Writes the rows in one transaction and returns how many were new.</summary>
    ValueTask<int> StoreAsync(IReadOnlyList<TradeFeatureRow> rows, DateTimeOffset createdAt, CancellationToken cancellationToken);

    /// <summary>The rows of the runs for the catalog version, ordered by run key then trade key.</summary>
    ValueTask<IReadOnlyList<TradeFeatureRow>> ReadAsync(IReadOnlyList<string> runKeys, string featureVersion, CancellationToken cancellationToken);

    /// <summary>The stored row of one trade for the catalog version, or null when none is stored (P8-11).</summary>
    ValueTask<TradeFeatureRow?> ReadOneAsync(string runKey, string tradeKey, string featureVersion, CancellationToken cancellationToken);
}
