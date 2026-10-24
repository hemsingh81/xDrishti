namespace XDrishti.Domain.Reporting;

/// <summary>One point of an equity curve: the running R after the trades that exited on a date, and how far it is below its running peak.</summary>
/// <param name="Date">The India date of the exits.</param>
/// <param name="CumulativeR">The running sum of R after those exits.</param>
/// <param name="DrawdownR">The running peak (never below 0) minus <paramref name="CumulativeR"/>; 0 at a new peak.</param>
/// <param name="Trades">Trades that exited on the date.</param>
public sealed record EquityPoint(DateOnly Date, double CumulativeR, double DrawdownR, int Trades);
