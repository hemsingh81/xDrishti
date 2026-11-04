using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The cost stress of CF-05 (ADR 0206): charges and slippage scaled by a multiplier, applied to the stored out-of-sample trades. Pure. A trade carries <c>RExact</c> and <c>SlippageR</c> (<c>RFilled - RExact</c>, never positive) and its charges in R
/// (<c>chargesR = RFilled - NetR</c>), so its net R at multiplier <c>m</c> is <c>RExact + m x (SlippageR - chargesR)</c>, which is <c>RExact + m x (NetR - RExact)</c>. The mean over a set is the mean of the per-trade values.
/// </summary>
public static class CostStress
{
    /// <summary>Printed with the figure.</summary>
    public const string Formula = "stressed net R = RExact + m x (SlippageR - chargesR) = RExact + m x (net R - RExact) for each stored out-of-sample trade, then averaged; m scales charges and slippage together";

    /// <summary>Printed with the figure: the limit of the restatement.</summary>
    public const string FirstOrderLimit = "exact for charges, first-order for slippage: a harsher slippage could also stop a fill from happening (the delay test covers that, this line does not); applied to the stored out-of-sample trades of the verdict, selection is not repeated; a fixed brokerage cap is over-penalised by scaling the total";

    /// <summary>The reason on every row while the sheet has not been checked against a bill (D5).</summary>
    public const string CostsNotChecked = "unavailable: costs not checked against a bill: stress gates cannot PASS";

    /// <summary>Net R of one trade with charges and slippage scaled by <paramref name="multiplier"/>.</summary>
    public static double StressedR(double rExact, double netR, double multiplier) => rExact + (multiplier * (netR - rExact));

    /// <summary>The mean stressed net R of the trades; null when there is none or a value is not a finite number.</summary>
    public static double? Mean(IReadOnlyList<StressTrade> trades, double multiplier)
    {
        ArgumentNullException.ThrowIfNull(trades);
        if (trades.Count == 0)
        {
            return null;
        }

        var sum = 0.0;
        foreach (var t in trades)
        {
            sum += StressedR(t.RExact, t.NetR, multiplier);
        }

        var mean = sum / trades.Count;
        return double.IsFinite(mean) ? mean : null;
    }

    /// <summary>
    /// The three points and the worst of them. 1x is read against the existing expectancy gate (<paramref name="oneXThreshold"/>, <paramref name="oneXPassed"/>, its own adjusted figure and verdict); the middle multiplier must reach at least its floor and the high one exceed its floor.
    /// Not multiplied by the trial factor: they are floors on realism, not significance tests.
    /// </summary>
    /// <param name="trades">The restated trades.</param>
    /// <param name="options">The settings.</param>
    /// <param name="oneXThreshold">The adjusted threshold of the existing expectancy gate.</param>
    /// <param name="oneXPassed">Whether the existing gate passed.</param>
    /// <param name="costsChecked">Whether the charge sheet is checked against a bill; false makes every row unavailable.</param>
    public static CostStressResult Evaluate(IReadOnlyList<StressTrade> trades, CostStressOptions options, double oneXThreshold, bool oneXPassed, bool costsChecked)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);
        var points = new[]
        {
            Point(trades, 1, oneXThreshold, ">=", costsChecked, (_, _) => oneXPassed),
            Point(trades, options.MidMultiplier, options.MidFloorR, ">=", costsChecked, (v, f) => v >= f),
            Point(trades, options.HighMultiplier, options.HighFloorR, ">", costsChecked, (v, f) => v > f),
        };
        var worst = points.Any(p => p.Status == StressStatus.Fail) ? StressStatus.Fail : points.Any(p => p.Status == StressStatus.Unavailable) ? StressStatus.Unavailable : StressStatus.Pass;
        return new CostStressResult(points, worst);
    }

    private static StressPoint Point(IReadOnlyList<StressTrade> trades, double m, double floor, string comparison, bool costsChecked, Func<double, double, bool> passes)
    {
        var mean = Mean(trades, m);
        if (mean is not { } value)
        {
            return new StressPoint(m, trades.Count, null, floor, comparison, StressStatus.Unavailable, trades.Count == 0 ? "unavailable: no out-of-sample trade" : "unavailable: a result is not a finite number");
        }

        var shown = Num(value);
        if (!costsChecked)
        {
            return new StressPoint(m, trades.Count, value, floor, comparison, StressStatus.Unavailable, CostsNotChecked);
        }

        var ok = passes(value, floor);
        return new StressPoint(m, trades.Count, value, floor, comparison, ok ? StressStatus.Pass : StressStatus.Fail, $"net expectancy {shown} R {(ok ? comparison : Opposite(comparison))} {Num(floor)} R ({(ok ? "met" : "missed")})");
    }

    private static string Opposite(string comparison) => comparison == ">=" ? "<" : "<=";

    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
