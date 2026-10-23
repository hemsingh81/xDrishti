namespace XDrishti.Domain.Checklist;

/// <summary>The ticket as the checklist sees it, sized for one account.</summary>
/// <param name="Symbol">The instrument.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="EntryPrice">The planned entry price.</param>
/// <param name="Quantity">Shares for this account.</param>
/// <param name="RiskPct">Risk of the ticket, percent of the account's capital.</param>
/// <param name="Sector">The sector; null when not known.</param>
public sealed record ChecklistTicket(string Symbol, string Side, double? EntryPrice, double? Quantity, double? RiskPct, string? Sector);
