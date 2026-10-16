namespace XDrishti.Application.Abstractions.Live;

/// <summary>A tick of the live feed (ADR 0024) reduced to what the database keeps: the last traded price of an instrument of the master list and the time the exchange stamped on it. The price is positive.</summary>
public sealed record LivePrice(Guid InstrumentId, decimal Price, DateTimeOffset TickAt);
