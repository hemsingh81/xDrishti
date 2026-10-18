namespace XDrishti.Simulation.Replay;

/// <summary>
/// The test of a price discontinuity (spec P3-04, Scope 6): <c>abs(open - previousClose) &gt; threshold × previousClose</c>, computed in <c>decimal</c> on the adjusted prices. The cast to <c>decimal</c> keeps a price written with at most
/// four decimals as written, and the adjustment rounds to four, so a move of exactly the threshold is not flagged. The <c>double</c> form <c>abs(open / close - 1) &gt; 0.2</c> is not used: it flags a close of 12.00 with an open of 9.60 and
/// a close of 1.50 with an open of 1.20 (both exactly -20 %, because <c>1.2 / 1.5 - 1</c> is 0.20000000000000007).
/// </summary>
internal static class Discontinuity
{
    // Beyond this a price cannot be a price of a share and the cast to decimal (28 digits) is not meaningful.
    private const double Largest = 1e15;

    // A threshold above this flags nothing a price can do; the clamp keeps the product with the largest price inside a decimal.
    private const double LargestThreshold = 1e12;

    /// <summary>
    /// Whether the open differs from the previous close by more than the threshold, as a share of that close. A previous close that is not above 0, or a price that is not a finite number or is absurdly large, cannot be compared
    /// and is flagged: the replay fails closed and restarts rather than carry a number it cannot judge.
    /// </summary>
    public static bool IsFlagged(double open, double previousClose, double threshold)
    {
        if (!double.IsFinite(open) || !double.IsFinite(previousClose) || previousClose <= 0 || previousClose > Largest || Math.Abs(open) > Largest)
        {
            return true;
        }

        var close = (decimal)previousClose;
        return Math.Abs((decimal)open - close) > (decimal)Math.Min(threshold, LargestThreshold) * close;
    }
}
