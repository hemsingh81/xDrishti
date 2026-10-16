namespace XDrishti.Application.Live.Push;

/// <summary>
/// Hub event <c>pricesChanged</c>: the prices that changed for the instruments the connection follows since the last message sent to it (the first messages after <c>Subscribe</c> carry all of them). At most one burst a second;
/// at most 500 instruments and 32 KB per message (a larger change comes as several messages with the same <c>asOf</c>). <c>asOf</c> is the server's time when it read the prices (UTC).
/// </summary>
public sealed record PricesChanged(DateTimeOffset AsOf, IReadOnlyList<InstrumentPrice> Instruments);
