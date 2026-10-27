namespace XDrishti.Application.Assistant;

/// <summary>
/// One holding read from the <c>aiv</c> views, with the classifications the tool groups by. <paramref name="Buckets"/> and <paramref name="Baskets"/> hold the names (a holding can sit in several baskets); an empty list means unclassified. Never carries an account id or a client id: the account is its label.
/// </summary>
public sealed record PortfolioHoldingRow(
    string Account,
    string Symbol,
    long Qty,
    decimal AvgCost,
    decimal LastPrice,
    DateTimeOffset? LastPriceAt,
    string Status,
    string? Sector,
    IReadOnlyList<string> Buckets,
    IReadOnlyList<string> Baskets,
    IReadOnlyList<PortfolioLotRow> Lots);
