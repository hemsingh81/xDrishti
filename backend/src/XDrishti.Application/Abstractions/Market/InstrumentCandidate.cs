using XDrishti.Domain.Market;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>One instrument the master list should contain after a refresh (already in scope, with canonical symbol, sector and flags).</summary>
public sealed record InstrumentCandidate(
    string CanonicalSymbol,
    InstrumentType Type,
    string ExchangeSegment,
    string Name,
    string DisplayName,
    string? Isin,
    string? Series,
    int LotSize,
    decimal? TickSize,
    DateOnly? ExpiryDate,
    string UnderlyingSymbol,
    string? Sector,
    string? SectorSource,
    bool FnoEligible,
    string? AsmGsmFlag,
    string? AsmGsmCategory,
    decimal? MtfLeverage,
    long? FreezeQuantity,
    string BrokerId);
