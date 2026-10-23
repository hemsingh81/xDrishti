using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>The frozen-model counterfactual with its out-of-sample evidence and the honest limits (P6-12, ADR 0077): does continued learning help?</summary>
/// <param name="Outcome">The conclusion.</param>
/// <param name="Reasons">Why, one sentence each.</param>
/// <param name="Options">The knobs used.</param>
/// <param name="Cut">The day the frozen model stopped learning (it saw only labels resolved before the period's first decision); null when no period could be made.</param>
/// <param name="To">The last day of the data used.</param>
/// <param name="FrozenAgeMonths">How long the frozen model is left alone, in months (the period the data allowed, at most the requested).</param>
/// <param name="FrozenTrainRows">Rows the frozen model was fitted on.</param>
/// <param name="Windows">The windows scored, in order.</param>
/// <param name="Judged">Both sides pooled over the informative windows (the basis of the judgement); null when there are none.</param>
/// <param name="All">Both sides pooled over every scored window; null when none.</param>
/// <param name="Limits">Sentences that must be printed with the result.</param>
public sealed record CounterfactualReport(
    CounterfactualOutcome Outcome,
    IReadOnlyList<string> Reasons,
    CounterfactualOptions Options,
    DateOnly? Cut,
    DateOnly To,
    int FrozenAgeMonths,
    int FrozenTrainRows,
    IReadOnlyList<CounterfactualWindow> Windows,
    CounterfactualPooled? Judged,
    CounterfactualPooled? All,
    IReadOnlyList<string> Limits)
{
    /// <summary>A fixed plain-text rendering; the same report gives the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(Cut is { } cut
            ? string.Create(c, $"frozen-model counterfactual: {Outcome}, frozen at {cut:yyyy-MM-dd} on {FrozenTrainRows} trades, {FrozenAgeMonths} months of stopped learning to {To:yyyy-MM-dd}, retrained every {Options.RetrainMonths} months")
            : "frozen-model counterfactual: " + Outcome);
        sb.AppendLine(string.Create(c, $"knobs: frozen_months {Options.FrozenMonths}, retrain_months {Options.RetrainMonths}, min_window_rows {Options.MinWindowRows}, min_judged_windows {Options.MinJudgedWindows}, min_gain_log_loss {Options.MinGainLogLoss:R}, significance_z {Options.SignificanceZ:R}, trials {Options.Trials}"));
        foreach (var reason in Reasons)
        {
            sb.AppendLine("  - " + reason);
        }

        foreach (var w in Windows)
        {
            sb.AppendLine(string.Create(c, $"window {w.Index} {w.Start:yyyy-MM-dd}..{w.End:yyyy-MM-dd}: {w.Rows} trades, {w.Days} days, {w.Instruments} instruments, retrained on {w.RetrainedTrainRows} (frozen {w.FrozenTrainRows}){(w.Informative ? string.Empty : ", same model on both sides, not judged")}"));
            sb.AppendLine("  " + Line(w.Pooled));
        }

        if (Judged is { } j)
        {
            sb.AppendLine("judged windows pooled: " + Line(j));
        }

        if (All is { } a)
        {
            sb.AppendLine("all windows pooled: " + Line(a));
        }

        foreach (var limit in Limits)
        {
            sb.AppendLine("limit: " + limit);
        }

        return sb.ToString();
    }

    private static string Line(CounterfactualPooled p)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(c, $"{p.Rows} trades; frozen log loss {p.Frozen.Metrics.LogLoss:F4} Brier {p.Frozen.Metrics.Brier:F4} AUC {Fmt(p.Frozen.Metrics.Auc)} take-R {p.Frozen.Policy.PolicyPerCandidate:F3} (kept {p.Frozen.Policy.Kept}, skipped mean R {Fmt(p.Frozen.Policy.MeanSkipped)}); retrained log loss {p.Retrained.Metrics.LogLoss:F4} Brier {p.Retrained.Metrics.Brier:F4} AUC {Fmt(p.Retrained.Metrics.Auc)} take-R {p.Retrained.Policy.PolicyPerCandidate:F3} (kept {p.Retrained.Policy.Kept}, skipped mean R {Fmt(p.Retrained.Policy.MeanSkipped)}); take-everything {p.Frozen.Policy.BaselinePerCandidate:F3} R; gain of retraining {p.MeanGain:F4} log loss (standard error {Fmt(p.GainStandardError)})");
    }

    private static string Fmt(double? v) => v is { } x ? x.ToString("F3", CultureInfo.InvariantCulture) : "n/a";
}
