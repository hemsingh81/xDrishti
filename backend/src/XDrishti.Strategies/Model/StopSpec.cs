using XDrishti.Domain.Market.Bars;

namespace XDrishti.Strategies.Model;

/// <summary>
/// The stop of a side (doc 11 §2, <i>Stops</i>): a <see cref="Kind"/> and the fields that kind uses. A field the kind does not use must be null (<c>field_not_used</c>), a required one must be set (<c>field_missing</c>).
/// <see cref="Timeframe"/> is the timeframe the stop is read on (the strategy's evaluation timeframe when null; not for <c>fixed_pct</c>).
/// </summary>
public sealed record StopSpec(
    StopKind Kind,
    Timeframe? Timeframe = null,
    Quantity? Pct = null,
    Quantity? Mult = null,
    Quantity? Lookback = null,
    Quantity? Minutes = null,
    Quantity? BufferAtr = null,
    Quantity? AtrPeriod = null);
