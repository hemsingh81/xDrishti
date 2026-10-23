using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>The meta-labelling policy of the champion with its out-of-sample evidence and the honest limits (P6-13, ADR 0075).</summary>
/// <param name="Outcome">The conclusion.</param>
/// <param name="Reasons">Why, one sentence each.</param>
/// <param name="Options">The knobs used.</param>
/// <param name="Rows">Development rows given.</param>
/// <param name="From">Earliest trade date.</param>
/// <param name="To">Latest trade date.</param>
/// <param name="UsedFolds">Usable folds.</param>
/// <param name="Folds">Every fold with its evaluation.</param>
/// <param name="Pooled">Kept and skipped R pooled over the evaluable folds (out of sample).</param>
/// <param name="Recent">The same over the last evaluated trades.</param>
/// <param name="Policy">The thresholds chosen on all development scores (the policy proposed); in-sample, so optimistic.</param>
/// <param name="Limits">Sentences that must be printed with the result.</param>
public sealed record MetaPolicyReport(
    MetaPolicyOutcome Outcome,
    IReadOnlyList<string> Reasons,
    MetaPolicyOptions Options,
    int Rows,
    DateOnly From,
    DateOnly To,
    int UsedFolds,
    IReadOnlyList<MetaPolicyFold> Folds,
    MetaPolicyTotals Pooled,
    MetaPolicyTotals Recent,
    IReadOnlyList<MetaPolicyThreshold> Policy,
    IReadOnlyList<string> Limits)
{
    /// <summary>The answer when there is nothing to evaluate (no champion, or not the champion's data).</summary>
    public static MetaPolicyReport NoPolicy(string reason, MetaPolicyOptions options, IReadOnlyList<string> limits) =>
        new(MetaPolicyOutcome.NoPolicy, [reason], options, 0, default, default, 0, [], MetaPolicyTotals.Empty, MetaPolicyTotals.Empty, [], limits);

    /// <summary>A fixed plain-text rendering; the same report gives the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        if (Outcome == MetaPolicyOutcome.NoPolicy)
        {
            sb.AppendLine("meta-labelling policy: NoPolicy");
        }
        else
        {
            sb.AppendLine(string.Create(c, $"meta-labelling policy: {Outcome}, {Rows} trades, {From:yyyy-MM-dd} to {To:yyyy-MM-dd}, {UsedFolds} usable folds, split {Options.Split}"));
            sb.AppendLine(string.Create(c, $"knobs: min_kept_share {Options.MinKeptShare:R}, threshold_step {Options.ThresholdStep:R}, min_threshold_rows {Options.MinThresholdRows}, min_lift_r {Options.MinLiftR:R}"));
        }

        foreach (var reason in Reasons)
        {
            sb.AppendLine("  - " + reason);
        }

        foreach (var f in Folds)
        {
            var v = f.Fold;
            if (!f.Evaluable)
            {
                sb.AppendLine(string.Create(c, $"fold {v.Index} {v.TestStart:yyyy-MM-dd}..{v.TestEnd:yyyy-MM-dd}: not evaluable (too few earlier scores to choose a threshold)"));
                continue;
            }

            sb.AppendLine(string.Create(c, $"fold {v.Index} {v.TestStart:yyyy-MM-dd}..{v.TestEnd:yyyy-MM-dd}: thresholds {Show(f.Thresholds)}; {Line(f.Totals)}"));
        }

        if (Folds.Count > 0)
        {
            sb.AppendLine("pooled out of sample: " + Line(Pooled));
            sb.AppendLine(string.Create(c, $"last {Recent.Candidates} evaluated: {Line(Recent)}"));
            sb.AppendLine("proposed thresholds (chosen on all development scores, in-sample): " + Show(Policy));
        }

        foreach (var limit in Limits)
        {
            sb.AppendLine("limit: " + limit);
        }

        return sb.ToString();
    }

    private static string Show(IReadOnlyList<MetaPolicyThreshold> thresholds) =>
        thresholds.Count == 0
            ? "none"
            : string.Join(", ", thresholds.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Group}={(t.Threshold is { } x ? x.ToString("F2", CultureInfo.InvariantCulture) : "take-all")} (n {t.Rows}, keeps {t.KeptShare:P0})")));

    private static string Line(MetaPolicyTotals t)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(c, $"{t.Candidates} candidates, kept {t.Kept} ({t.KeptShare:P0}) mean R {Fmt(t.MeanKept)}, skipped {t.Skipped} mean R {Fmt(t.MeanSkipped)}, take-everything {t.BaselinePerCandidate:F3} R, policy {t.PolicyPerCandidate:F3} R per candidate, lift {t.Lift:F3}");
    }

    private static string Fmt(double? v) => v is { } x ? x.ToString("F3", CultureInfo.InvariantCulture) : "n/a";
}
