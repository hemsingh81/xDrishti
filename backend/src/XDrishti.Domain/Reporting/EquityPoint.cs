namespace XDrishti.Domain.Reporting;

/// <summary>One point of an equity curve: the running R after the trades that exited on a date, and the deepest fall from the running peak reached on that date.</summary>
/// <param name="Date">The India date of the exits.</param>
/// <param name="CumulativeR">The running sum of R after those exits (the end of the date).</param>
/// <param name="DrawdownR">The deepest running peak (never below 0) minus running R reached at any exit of the date, including between two trades of the same date; 0 at a new peak. The largest of these is the maximum drawdown of the group.</param>
/// <param name="Trades">Trades that exited on the date.</param>
public sealed record EquityPoint(DateOnly Date, double CumulativeR, double DrawdownR, int Trades);
