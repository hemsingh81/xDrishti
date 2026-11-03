namespace XDrishti.Application.Abstractions.Market.Eod;

/// <summary>One security-day of the daily table (logical fields; both NSE layouts map to this). Delivery fields are null when the delivery file had no row.</summary>
public sealed record EodRow(
    DateOnly TradeDate,
    string Series,
    string Symbol,
    string? Isin,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal? PrevClose,
    long TradedQty,
    decimal Turnover,
    long? Trades,
    long? DeliveryQty,
    decimal? DeliveryPct);

/// <summary>A member of <c>market.eod_universe(asOf)</c>.</summary>
public sealed record EodUniverseRow(string? Isin, string Symbol, string Series, DateOnly KnownSince, DateOnly LastSeen, bool InCatalog);

/// <summary>One daily row set against the minute data of the same session (for the quality finding).</summary>
public sealed record DailyMinuteRow(string Symbol, DateOnly TradeDate, long DailyQty, long MinuteVolume, decimal DailyHigh, decimal MinuteHigh, decimal TickSize);
