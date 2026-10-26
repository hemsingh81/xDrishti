using System.Globalization;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The two paper exit statistics as verdicts with labels (spec P10-04, criteria 6 to 9). Both need 60 counted trades, costs confirmed by the owner, an R after costs and a band that could be drawn; otherwise the verdict is
/// <c>not_evaluable</c> with every reason. Pure.
/// </summary>
public static class PaperExitStatistics
{
    /// <summary>The fewest counted trades for any figure.</summary>
    public const int ProvisionalFrom = 30;

    /// <summary>The fewest counted trades for a plain value.</summary>
    public const int ValueFrom = 60;

    /// <summary>The sentence of the early warning.</summary>
    public const string EarlyWarningText = "paper is clearly worse than the backtest allows: stop and review before more trades";

    /// <summary>The state of a figure computed from <paramref name="n"/> counted trades: insufficient, provisional or value.</summary>
    public static string StateOf(int n) => n < ProvisionalFrom ? "insufficient" : n < ValueFrom ? "provisional" : "value";

    /// <summary>Evaluates E3, E4 and the early warning.</summary>
    /// <param name="n">Counted trades.</param>
    /// <param name="netBasis">Whether every counted R is after costs.</param>
    /// <param name="costsConfirmed">Whether the owner confirmed the charge sheet(s) (the port's fact).</param>
    /// <param name="band">The band, or null when it could not be drawn.</param>
    /// <param name="bandProblem">Why the band is absent (null when present).</param>
    public static ExitCriteria Evaluate(int n, bool netBasis, bool costsConfirmed, BandResult? band, string? bandProblem)
    {
        var problems = new List<string>();
        if (n < ValueFrom)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{n} counted trades, {ValueFrom} are needed"));
        }

        if (!netBasis)
        {
            problems.Add("the R is gross, not after costs");
        }

        if (!costsConfirmed)
        {
            problems.Add("costs not confirmed");
        }

        if (band is null)
        {
            problems.Add(bandProblem ?? "no backtest band");
        }

        if (problems.Count > 0 || band is null)
        {
            var no = new CriterionResult(CriterionResult.NotEvaluable, "not evaluable: " + string.Join("; ", problems));
            return new ExitCriteria(no, no, Warning(n, band));
        }

        return new ExitCriteria(Expectancy(band), Drawdown(band), Warning(n, band));
    }

    private static string? Warning(int n, BandResult? band) =>
        n >= ProvisionalFrom && band is not null && band.IntervalHigh < band.BandP05 ? EarlyWarningText : null;

    private static CriterionResult Expectancy(BandResult b)
    {
        var range = $"[{R(b.BandP05)}, {R(b.BandP95)}]";
        var facts = new List<string>();
        if (b.PaperMean <= 0)
        {
            facts.Add($"paper mean {R(b.PaperMean)} is not above 0");
        }

        if (b.Position == "below")
        {
            facts.Add($"paper mean {R(b.PaperMean)} is below the backtest band {range}");
        }

        if (facts.Count > 0)
        {
            return new CriterionResult(CriterionResult.NotMet, string.Join("; ", facts) + string.Create(CultureInfo.InvariantCulture, $" (n = {b.N})"));
        }

        var label = $"paper mean {R(b.PaperMean)} is above 0 and not below the backtest band {range}";
        if (b.IntervalLow <= 0)
        {
            label += $" (point estimate; 90% interval [{R(b.IntervalLow)}, {R(b.IntervalHigh)}] includes 0: positive expectancy is not statistically established)";
        }

        if (b.Position == "above")
        {
            label += " (above the band: luck or a leak is possible)";
        }

        return new CriterionResult(CriterionResult.Met, label);
    }

    private static CriterionResult Drawdown(BandResult b)
    {
        var numbers = $"paper largest fall {Plain(b.PaperMaxDrawdown)}R, backtest 90th percentile at {b.N.ToString(CultureInfo.InvariantCulture)} trades {Plain(b.DrawdownP90)}R";
        return b.DrawdownPosition == "within"
            ? new CriterionResult(CriterionResult.Met, numbers + ": within")
            : new CriterionResult(CriterionResult.NotMet, numbers + ": worse than the 90th percentile");
    }

    private static string R(double value) => value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "R";

    private static string Plain(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
}
