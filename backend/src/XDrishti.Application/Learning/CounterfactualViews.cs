using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>One side of the counterfactual on a set of rows, as stored and shown (P8-12). A metric that cannot be computed is null, never NaN.</summary>
/// <param name="LogLoss">Out-of-sample log loss.</param>
/// <param name="Brier">Brier score.</param>
/// <param name="Auc">Area under the ROC curve; null when one class is missing.</param>
/// <param name="TakeR">R per candidate of taking what the side scores at or above its own train base rate.</param>
/// <param name="Kept">Trades taken.</param>
/// <param name="Skipped">Trades skipped.</param>
/// <param name="SkippedMeanR">Mean R of the skipped trades; null when none.</param>
public sealed record CounterfactualSideView(double? LogLoss, double? Brier, double? Auc, double? TakeR, int Kept, int Skipped, double? SkippedMeanR);

/// <summary>Both sides pooled over some windows.</summary>
/// <param name="Rows">Rows scored by both sides.</param>
/// <param name="Frozen">The frozen side.</param>
/// <param name="Retrained">The retrained side.</param>
/// <param name="Gain">Mean paired log loss gain of retraining (positive: retraining was better).</param>
/// <param name="GainStandardError">Standard error of the gain clustered by trade day; null with fewer than two days.</param>
/// <param name="TakeRUplift">Rows times the take-R difference, in R.</param>
public sealed record CounterfactualPooledView(int Rows, CounterfactualSideView Frozen, CounterfactualSideView Retrained, double? Gain, double? GainStandardError, double? TakeRUplift);

/// <summary>One retrain window.</summary>
/// <param name="Index">1-based.</param>
/// <param name="Start">First day.</param>
/// <param name="End">Last day.</param>
/// <param name="Rows">Rows scored.</param>
/// <param name="Days">Distinct trade days.</param>
/// <param name="Instruments">Distinct instruments.</param>
/// <param name="FrozenTrainRows">Rows the frozen model was fitted on.</param>
/// <param name="RetrainedTrainRows">Rows the retrained model was fitted on.</param>
/// <param name="Informative">False: the same model on both sides, not judged.</param>
/// <param name="Frozen">The frozen side.</param>
/// <param name="Retrained">The retrained side.</param>
/// <param name="Gain">Paired log loss gain; null when not informative.</param>
/// <param name="GainStandardError">Its standard error clustered by day.</param>
/// <param name="TakeRUplift">Take-R uplift of the window in R; null when not informative (a zero-width bar, never a bar of 0).</param>
/// <param name="CumulativeTakeRUplift">The running total of the uplifts up to this window, in R.</param>
/// <param name="Note">A sentence for a window that is not judged, or null.</param>
public sealed record CounterfactualWindowView(
    int Index,
    DateOnly Start,
    DateOnly End,
    int Rows,
    int Days,
    int Instruments,
    int FrozenTrainRows,
    int RetrainedTrainRows,
    bool Informative,
    CounterfactualSideView Frozen,
    CounterfactualSideView Retrained,
    double? Gain,
    double? GainStandardError,
    double? TakeRUplift,
    double CumulativeTakeRUplift,
    string? Note);

/// <summary>The knobs the result was made with.</summary>
public sealed record CounterfactualKnobsView(int FrozenMonths, int RetrainMonths, int MinWindowRows, int MinJudgedWindows, double MinGainLogLoss, double SignificanceZ, int Trials, double PolicyTolerance, int HoldOutMonths);

/// <summary>
/// The stored counterfactual as the API shows it (P8-12, <c>schemaVersion</c> 1): the view of the domain report, so NaN cannot enter the JSON. Every statistic is a server field; the screen computes none.
/// </summary>
/// <param name="SchemaVersion">1.</param>
/// <param name="Id">The row id; null inside the stored JSON (the row id is added on read).</param>
/// <param name="Outcome">Inconclusive, NoDifference, RetrainingHelps or FrozenBetter.</param>
/// <param name="Reasons">Why, one sentence each.</param>
/// <param name="Cut">The day the frozen model stopped learning; null when no period could be made.</param>
/// <param name="To">The last day of the data used.</param>
/// <param name="FrozenAgeMonths">Months of stopped learning.</param>
/// <param name="FrozenTrainRows">Rows the frozen model was fitted on.</param>
/// <param name="Knobs">The knobs.</param>
/// <param name="Windows">The windows in order.</param>
/// <param name="JudgedWindows">Windows that were judged.</param>
/// <param name="Judged">Both sides pooled over the judged windows.</param>
/// <param name="All">Both sides pooled over every scored window.</param>
/// <param name="Limits">Sentences that must be shown with the result.</param>
/// <param name="Basis">gross, costed_unconfirmed or net_confirmed.</param>
/// <param name="Sources">The runs behind the report.</param>
/// <param name="Text">The printed text (also kept in the stored JSON; bounded: a handful of windows, one line each).</param>
/// <param name="BuiltBy">Who built it; null inside the stored JSON.</param>
/// <param name="CreatedAt">When it was stored; null inside the stored JSON.</param>
/// <param name="SameRunsStored">How many stored counterfactuals use the same runs (every re-run is another trial); 0 inside the stored JSON.</param>
public sealed record CounterfactualReportView(
    int SchemaVersion,
    long? Id,
    string Outcome,
    IReadOnlyList<string> Reasons,
    DateOnly? Cut,
    DateOnly To,
    int FrozenAgeMonths,
    int FrozenTrainRows,
    CounterfactualKnobsView Knobs,
    IReadOnlyList<CounterfactualWindowView> Windows,
    int JudgedWindows,
    CounterfactualPooledView? Judged,
    CounterfactualPooledView? All,
    IReadOnlyList<string> Limits,
    string Basis,
    IReadOnlyList<CellSource> Sources,
    string Text,
    string? BuiltBy,
    DateTimeOffset? CreatedAt,
    int SameRunsStored)
{
    /// <summary>The view version written to <c>report_json</c>.</summary>
    public const int Version = 1;

    /// <summary>The view of a domain report. NaN and infinity become null.</summary>
    internal static CounterfactualReportView Of(CounterfactualReport r, int holdOutMonths, string basis, IReadOnlyList<CellSource> sources, string text)
    {
        var o = r.Options;
        var uplift = CounterfactualUplift.Compute([.. r.Windows.Select(w => new UpliftWindow(w.Rows, w.Pooled.Frozen.Policy.PolicyPerCandidate, w.Pooled.Retrained.Policy.PolicyPerCandidate, w.Informative))]);
        var windows = r.Windows.Select((w, i) => new CounterfactualWindowView(
            w.Index, w.Start, w.End, w.Rows, w.Days, w.Instruments, w.FrozenTrainRows, w.RetrainedTrainRows, w.Informative,
            Side(w.Pooled.Frozen), Side(w.Pooled.Retrained),
            w.Informative ? Finite(w.Pooled.MeanGain) : null, w.Informative ? Finite(w.Pooled.GainStandardError) : null,
            Finite(uplift[i].Uplift), Finite(uplift[i].Cumulative) ?? 0,
            w.Informative ? null : "Same model on both sides, not judged.")).ToList();
        return new CounterfactualReportView(
            Version, null, r.Outcome.ToString(), r.Reasons, r.Cut, r.To, r.FrozenAgeMonths, r.FrozenTrainRows,
            new CounterfactualKnobsView(o.FrozenMonths, o.RetrainMonths, o.MinWindowRows, o.MinJudgedWindows, o.MinGainLogLoss, o.SignificanceZ, o.Trials, o.PolicyTolerance, holdOutMonths),
            windows, r.Windows.Count(w => w.Informative), Pooled(r.Judged), Pooled(r.All), r.Limits, basis, sources, text, null, null, 0);
    }

    private static CounterfactualPooledView? Pooled(CounterfactualPooled? p) =>
        p is null ? null : new CounterfactualPooledView(p.Rows, Side(p.Frozen), Side(p.Retrained), Finite(p.MeanGain), Finite(p.GainStandardError),
            Finite(p.Rows * (p.Retrained.Policy.PolicyPerCandidate - p.Frozen.Policy.PolicyPerCandidate)));

    private static CounterfactualSideView Side(CounterfactualSide s) =>
        new(Finite(s.Metrics.LogLoss), Finite(s.Metrics.Brier), Finite(s.Metrics.Auc), Finite(s.Policy.PolicyPerCandidate), s.Policy.Kept, s.Policy.Skipped, Finite(s.Policy.MeanSkipped));

    private static double? Finite(double? v) => v is { } x && double.IsFinite(x) ? x : null;
}

/// <summary>A stored counterfactual in a list (P8-12): the header only.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Outcome">The conclusion.</param>
/// <param name="AsOf">The last day of the data used.</param>
/// <param name="Basis">gross, costed_unconfirmed or net_confirmed.</param>
/// <param name="CreatedAt">When it was stored.</param>
/// <param name="BuiltBy">Who built it.</param>
/// <param name="Windows">Windows scored.</param>
/// <param name="JudgedWindows">Windows judged.</param>
/// <param name="SameRunsStored">How many stored counterfactuals use the same runs.</param>
public sealed record CounterfactualHeaderView(long Id, string Outcome, DateOnly AsOf, string Basis, DateTimeOffset CreatedAt, string BuiltBy, int Windows, int JudgedWindows, int SameRunsStored);
