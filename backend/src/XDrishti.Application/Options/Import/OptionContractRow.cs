namespace XDrishti.Application.Options.Import;

/// <summary>
/// One option row of Dhan's scrip master whose underlying is listed, read and checked: the strike in integer paise (rupees x 100, half away from zero), the side
/// <c>CE</c>/<c>PE</c>, the expiry kind <c>week</c>/<c>month</c> or null when the flag is something else, the tick in paise or null.
/// </summary>
internal sealed record OptionContractRow(
    long SecurityId,
    string UnderlyingSymbol,
    string Instrument,
    DateOnly Expiry,
    int StrikePx,
    string Side,
    string? ExpiryKind,
    int LotSize,
    int? TickPx);
