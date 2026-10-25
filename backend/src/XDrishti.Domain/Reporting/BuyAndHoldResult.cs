namespace XDrishti.Domain.Reporting;

/// <summary>The equal-weight price return of a set of instruments over a window (P8-07).</summary>
/// <param name="Return">The mean return in percent; a number only when enough instruments were included.</param>
/// <param name="Included">Instruments the figure rests on.</param>
/// <param name="Excluded">Instruments left out, in symbol order.</param>
public sealed record BuyAndHoldResult(Metric Return, int Included, IReadOnlyList<BuyAndHoldExclusion> Excluded);
