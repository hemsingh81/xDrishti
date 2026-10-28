using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// One fill of the owner's day at the broker (spec P11-01, ADR 0149): read from the trade book, every product of the NSE cash segment. Plain data; nothing here can reach a broker. The <see cref="Id"/> is ours (stored beside the broker's trade id).
/// </summary>
/// <param name="Id">Our id of the stored row (the matcher's key).</param>
/// <param name="AccountId">The live account.</param>
/// <param name="BrokerTradeId">The exchange's trade id (unique with the account).</param>
/// <param name="BrokerOrderId">The order the trade belongs to, when the broker says.</param>
/// <param name="Symbol">The normalised ticker.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="Product">The broker's product (CNC, INTRADAY, MTF ...), upper case.</param>
/// <param name="Qty">Shares, above zero.</param>
/// <param name="Price">Rupees per share, above zero.</param>
/// <param name="Date">India date.</param>
/// <param name="Time">India time, null when the broker sent none.</param>
public sealed record LiveFill(Guid Id, Guid AccountId, string BrokerTradeId, string? BrokerOrderId, string Symbol, Side Side, string Product, long Qty, decimal Price, DateOnly Date, TimeOnly? Time);
