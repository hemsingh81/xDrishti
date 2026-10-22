using System.Text;
using static XDrishti.Application.Lab.LabReportBuilder;

namespace XDrishti.Application.Lab;

/// <summary>
/// The plain-English summary of a report (spec P5-06): a fixed template over the report's own numbers. Every number printed is a raw value of the report, never one computed from others; no date, no clock, no assistant. It quotes the verdict and
/// never advises.
/// </summary>
internal static class ReportSummary
{
    public static string Write(LabReportView r)
    {
        var s = new StringBuilder();
        var k = r.Kpis;
        s.Append("The ").Append(r.Run.Method).Append(" test of strategy ").Append(r.Run.StrategyId).Append(" ended as ").Append(r.Outcome).Append('.');

        s.Append(" Out of sample it made ").Append(k.OosTrades).Append(" trades");
        Append(s, k.ExpectancyR, "an expectancy of ", " R");
        Append(s, k.ProfitFactor, "a profit factor of ", string.Empty);
        Append(s, k.MaxDrawdownR, "a maximum drawdown of ", " R");
        s.Append(", with ").Append(k.PositiveFolds).Append(" positive folds of ").Append(k.Folds).Append('.');

        var missed = r.Gates.Where(g => !g.Passed).Select(g => g.Name).ToList();
        s.Append(missed.Count == 0 ? " Every gate was met." : " Gates missed: " + string.Join(", ", missed) + ".");
        s.Append(" The family has ").Append(r.VariantCount).Append(" tested variants, so the thresholds were tightened by a factor of ").Append(F(r.AdjustmentFactor)).Append('.');

        if (r.BestCells.Count > 0 && r.BestCells[0].OosMeanR is { } best)
        {
            var c = r.BestCells[0];
            s.Append(" The best cell was ").Append(c.Cell).Append(" with a mean of ").Append(F(best)).Append(" R over ").Append(c.OosTrades).Append(" out-of-sample trades.");
        }

        if (r.Robustness?.SignFlipBaseline is { } b)
        {
            s.Append(" The sign-flip baseline gave a p-value of ").Append(P(b.PValue)).Append('.');
        }

        if (r.Robustness?.MonteCarlo is { } m)
        {
            s.Append(" Resampling the trades put the 5th percentile of the mean at ").Append(F(m.MeanP05)).Append(" R.");
        }

        if (r.Warnings.Count > 0)
        {
            s.Append(" Warnings: ").Append(string.Join(", ", r.Warnings.Select(w => w.Code).Distinct(StringComparer.Ordinal))).Append('.');
        }

        s.Append(" This restates the numbers of this report, rests on a short history without a hold-out, and is not advice.");
        return s.ToString();
    }

    private static void Append(StringBuilder s, double? value, string lead, string unit)
    {
        if (value is { } v)
        {
            s.Append(", ").Append(lead).Append(F(v)).Append(unit);
        }
    }
}
