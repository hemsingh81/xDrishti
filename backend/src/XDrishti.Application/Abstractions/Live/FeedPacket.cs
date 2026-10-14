namespace XDrishti.Application.Abstractions.Live;

/// <summary>What kind of packet the feed sent. Anything else is not parsed (it is dropped and counted by the connection).</summary>
public enum FeedPacketKind
{
    /// <summary>Last traded price and its time (response code 2).</summary>
    Ticker,

    /// <summary>Last traded price and time plus volume and the day's open, high, low and close (response code 4).</summary>
    Quote,

    /// <summary>The previous session's close (response code 6). Parsed but not trusted until the owner's first real session has shown its meaning (ADR 0024 §3).</summary>
    PreviousClose,

    /// <summary>The server is disconnecting, with a reason code (response code 50).</summary>
    Disconnect,
}

/// <summary>
/// One parsed packet of Dhan's market feed (ADR 0024). <see cref="Price"/> is the last price (or, for <see cref="FeedPacketKind.PreviousClose"/>, the previous close) and is positive for every kind but <see cref="FeedPacketKind.Disconnect"/>;
/// <see cref="TradeTime"/> is set for ticker and quote packets. <see cref="Volume"/>, <see cref="Open"/>, <see cref="High"/>, <see cref="Low"/> and <see cref="Close"/> belong to quote packets (null when the packet gave none that is usable).
/// <see cref="Reason"/> is the disconnect reason code.
/// </summary>
public sealed record FeedPacket(
    FeedPacketKind Kind,
    FeedSecurity Security,
    decimal Price = 0m,
    DateTimeOffset? TradeTime = null,
    long? Volume = null,
    decimal? Open = null,
    decimal? High = null,
    decimal? Low = null,
    decimal? Close = null,
    int Reason = 0);
