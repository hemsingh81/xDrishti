using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Alerts;

// Request and response shapes of the price-alert API (ADR 0022). Prices and levels are in rupees; times are UTC instants.

/// <summary>A new price alert: the master-list share or ETF, <c>Direction</c> <c>at_or_below|at_or_above</c>, the <c>Level</c> in rupees (above 0, at most 10,000,000, at most 4 decimals) and an optional plain-text <c>Note</c> of at most 200 characters.</summary>
public sealed record PriceAlertInput(Guid? InstrumentId, string? Direction, decimal? Level, string? Note);

/// <summary>
/// A price alert. <c>Status</c> is <c>active</c>, <c>triggered</c> (with <c>TriggeredAt</c> and <c>TriggerPrice</c>, the last price of the tick that hit the level) or <c>cancelled</c>. It is checked on the last price of each quotes tick only (every 5 minutes while the market
/// is open): a spike between two ticks is not seen.
/// </summary>
public sealed record PriceAlertView(
    Guid Id,
    Guid InstrumentId,
    string Symbol,
    string Name,
    string Direction,
    decimal Level,
    string? Note,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? TriggeredAt,
    decimal? TriggerPrice);

/// <summary>One page of alerts, newest first (<c>Total</c> counts all matches). <c>ActiveCount</c> of <c>ActiveLimit</c> alerts are active, whatever the filter.</summary>
public sealed record PriceAlertPage(IReadOnlyList<PriceAlertView> Items, int Page, int PageSize, int Total, int ActiveCount, int ActiveLimit);

internal static class PriceAlertViews
{
    public static PriceAlertView Of(AlertRow row) => new(
        row.Id,
        row.InstrumentId,
        row.Symbol,
        row.Name,
        row.Direction.ToCode(),
        row.Level,
        row.Note,
        row.Status.ToString().ToLowerInvariant(),
        row.CreatedAt,
        row.TriggeredAt,
        row.TriggerPrice);
}
