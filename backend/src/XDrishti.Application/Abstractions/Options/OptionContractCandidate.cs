namespace XDrishti.Application.Abstractions.Options;

/// <summary>An option contract to keep, its underlying resolved to the master list (columns of <c>options.contracts</c>; strike and tick in paise).</summary>
public sealed record OptionContractCandidate(
    long SecurityId,
    Guid UnderlyingId,
    string Instrument,
    DateOnly Expiry,
    int StrikePx,
    string Side,
    string? ExpiryKind,
    int LotSizeSeen,
    int? TickPx);
