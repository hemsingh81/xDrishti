namespace XDrishti.Domain.Portfolio;

/// <summary>
/// What the broker reports for one holding: quantity, average cost and last traded price, but no purchase date. Rows with the same
/// <see cref="Key"/> are one holding.
/// </summary>
public sealed record BrokerHolding(
    string Exchange,
    string SecurityId,
    string? Isin,
    string TradingSymbol,
    long Qty,
    decimal AvgCost,
    decimal LastPrice)
{
    /// <summary>The ISIN when the broker gives one (upper case), otherwise <c>{securityId}:{exchange}</c>. The identity used in import requests.</summary>
    public string Key => KeyOf(Isin, SecurityId, Exchange);

    public static string KeyOf(string? isin, string securityId, string exchange) =>
        string.IsNullOrWhiteSpace(isin) ? $"{securityId.Trim()}:{exchange.Trim().ToUpperInvariant()}" : isin.Trim().ToUpperInvariant();

    /// <summary>
    /// One row per holding: rows with the same key are added up (quantity summed, average cost weighted by quantity, the first row's price). Rows
    /// without a positive quantity are dropped. The order of first appearance is kept.
    /// </summary>
    public static IReadOnlyList<BrokerHolding> Merge(IEnumerable<BrokerHolding> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var order = new List<string>();
        var byKey = new Dictionary<string, BrokerHolding>(StringComparer.Ordinal);
        foreach (var row in rows.Where(r => r.Qty > 0))
        {
            if (!byKey.TryGetValue(row.Key, out var existing))
            {
                order.Add(row.Key);
                byKey[row.Key] = row;
                continue;
            }

            var qty = existing.Qty + row.Qty;
            byKey[row.Key] = existing with { Qty = qty, AvgCost = ((existing.Qty * existing.AvgCost) + (row.Qty * row.AvgCost)) / qty };
        }

        return [.. order.Select(k => byKey[k])];
    }
}
