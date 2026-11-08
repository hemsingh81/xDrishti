namespace XDrishti.Domain.Reporting;

/// <summary>The daily returns of an equal-weight hold (CF-11 row 13). <paramref name="Reason"/> is empty when there are returns, else why there are none.</summary>
/// <param name="Days">The sessions, oldest first.</param>
/// <param name="Returns">The return of the hold on each session against the session before (0.01 = 1 %), in the order of <paramref name="Days"/>.</param>
/// <param name="Reason">Why there is no series; empty when there is one.</param>
public sealed record BuyAndHoldDailyResult(IReadOnlyList<DateOnly> Days, IReadOnlyList<double> Returns, string Reason);
