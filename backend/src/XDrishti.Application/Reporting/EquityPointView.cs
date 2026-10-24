namespace XDrishti.Application.Reporting;

/// <summary>One point of an equity curve: the running R after the exits of a date and its fall from the running peak.</summary>
/// <param name="Date">The India date of the exits.</param>
/// <param name="CumulativeR">The running R.</param>
/// <param name="DrawdownR">The running peak minus the running R.</param>
/// <param name="Trades">Trades that exited on the date.</param>
public sealed record EquityPointView(DateOnly Date, double CumulativeR, double DrawdownR, int Trades);
