namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>An instrument of the master as the list matching sees it.</summary>
public sealed record MasterInstrument(Guid Id, string Symbol, string? Isin, DateTimeOffset? RetiredAt);
