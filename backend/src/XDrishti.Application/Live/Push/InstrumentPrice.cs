namespace XDrishti.Application.Live.Push;

/// <summary>
/// The latest known price of an instrument: <c>lastPrice</c> at <c>priceAt</c> (the time of the exchange's tick for <c>live</c>, the time of the quote for <c>quote</c>, the time of the sync for <c>sync</c>; UTC) and where it came from:
/// <c>live</c> (the live feed), <c>quote</c> (the 5-minute quote poll) or <c>sync</c> (the broker's holdings list). The same values as <c>lastPrice</c>, <c>priceAt</c> and <c>priceSource</c> of the holdings list. An older price never follows a newer one
/// for the same instrument; a client that gets one anyway ignores it.
/// </summary>
public sealed record InstrumentPrice(Guid InstrumentId, decimal LastPrice, DateTimeOffset PriceAt, string Source);
