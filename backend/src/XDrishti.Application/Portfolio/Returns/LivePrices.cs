namespace XDrishti.Application.Portfolio.Returns;

/// <summary>Today's point at the current prices (quote or sync): the India date, the price of each instrument (by instrument id) and the time of the newest of them.</summary>
public sealed record LivePrices(DateOnly Date, IReadOnlyDictionary<Guid, decimal> Prices, DateTimeOffset At);
