using System.Globalization;

namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>
/// The what-if of the down scale over stored trades (spec BK-10, criteria 11 and 12). Pure: the same trades, each R multiplied by its scale (never above 1, so there is no up-scaling what-if). Prints facts, recommends nothing.
/// </summary>
public static class SizingStudy
{
    /// <summary>The fewest trades a half needs to be judged.</summary>
    public const int MinTradesPerHalf = 30;

    private const double Tolerance = 1e-9;

    /// <summary>Compares the trades (in decision order) with and without their scale.</summary>
    public static SizingStudyResult Compare(IReadOnlyList<StudyTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var plain = trades.Select(t => t.NetR).ToList();
        var scaled = trades.Select(t => t.NetR * Clamp(t.Scale)).ToList();
        var (plainTotal, plainDrawdown) = Walk(plain);
        var (scaledTotal, scaledDrawdown) = Walk(scaled);
        var drawdownChange = Change(plainDrawdown, scaledDrawdown);
        var totalChange = Change(plainTotal, scaledTotal);
        var half = trades.Count / 2;
        IReadOnlyList<StudyHalf> halves = [Half(plain[..half], scaled[..half]), Half(plain[half..], scaled[half..])];
        var summary = $"max drawdown {Words(drawdownChange)}, total R {Words(totalChange)}; {SizingStudyResult.Caveat}";
        return new SizingStudyResult(trades.Count, plainTotal, scaledTotal, plainDrawdown, scaledDrawdown, drawdownChange, totalChange, halves, summary);
    }

    private static double Clamp(double scale) => double.IsFinite(scale) ? Math.Clamp(scale, 0, VolTarget.Ceiling) : 1.0;

    private static (double Total, double Drawdown) Walk(IReadOnlyList<double> rs)
    {
        double cumulative = 0, peak = 0, worst = 0;
        foreach (var r in rs)
        {
            cumulative += r;
            peak = Math.Max(peak, cumulative);
            worst = Math.Max(worst, peak - cumulative);
        }

        return (cumulative, worst);
    }

    private static StudyHalf Half(List<double> plain, List<double> scaled)
    {
        if (plain.Count < MinTradesPerHalf)
        {
            return new StudyHalf(plain.Count, false, null, null);
        }

        var (pt, pd) = Walk(plain);
        var (st, sd) = Walk(scaled);
        return new StudyHalf(plain.Count, true, Sign(sd - pd), Sign(st - pt));
    }

    private static int Sign(double d) => Math.Abs(d) < Tolerance ? 0 : Math.Sign(d);

    private static double? Change(double before, double after) => Math.Abs(before) < Tolerance ? null : (after - before) / Math.Abs(before) * 100;

    private static string Words(double? change) => change switch
    {
        null => "unchanged or not comparable",
        { } c when Math.Abs(c) < 0.05 => "unchanged",
        { } c when c < 0 => string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(c):0.#} % lower"),
        { } c => string.Create(CultureInfo.InvariantCulture, $"{c:0.#} % higher"),
    };
}
