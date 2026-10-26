namespace XDrishti.Domain.Market.Context;

/// <summary>
/// The market context as of a date (ADR 0033): <paramref name="AsOf"/> is the day the values belong to (the latest stored day strictly before the date asked), <paramref name="Basis"/> is <see cref="ContextBasis"/>, and a value that is
/// not ready is null, never a default. <paramref name="Breadth"/> is null until P3-01e.
/// </summary>
public sealed record ContextFigures(
    DateOnly AsOf,
    string Basis,
    double? IndexTrend,
    double? IndexAdx,
    double? IndexVolPct,
    double? Vix,
    double? VixPct,
    double? VixChg,
    double? RegimeTrend,
    double? RegimeVol,
    double? Regime,
    double? Breadth);
