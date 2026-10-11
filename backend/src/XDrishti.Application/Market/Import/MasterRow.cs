using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Import;

/// <summary>One in-scope row of Dhan's scrip master, read and checked (nothing derived yet).</summary>
internal sealed record MasterRow(
    InstrumentType Type,
    string Segment,
    string Instrument,
    string SecurityId,
    string? Isin,
    string UnderlyingSymbol,
    string SymbolName,
    string DisplayName,
    string? Series,
    int LotSize,
    decimal? TickSize,
    DateOnly? Expiry,
    string? AsmGsmFlag,
    string? AsmGsmCategory,
    decimal? MtfLeverage,
    long? FreezeQuantity);
