namespace XDrishti.Indicators;

/// <summary>
/// One day of the market context (P3-01c, ADR 0033): the India trading <see cref="Date"/>, the high, low and close of the regime index (Nifty 50 by default), the close of the volatility index when it is known
/// (<see cref="VixClose"/>, null when the day has none: it is never filled from another day) and the breadth when it is known (<see cref="Breadth"/>, a percent from 0 to 100: the share of the tracked stocks
/// above their 20-day average, computed by the context data job). <see cref="MarketContext"/> refuses a record that is not a possible day: a non-positive or non-finite index price, a high below the low, a
/// close outside the high / low, a non-positive VIX, a breadth outside 0 to 100.
/// </summary>
public sealed record ContextDay(DateOnly Date, double IndexHigh, double IndexLow, double IndexClose, double? VixClose = null, double? Breadth = null);
