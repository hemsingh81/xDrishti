using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The knobs an entry-window study was made with.</summary>
public sealed record EntryWindowKnobsView(int BucketMinutes, int MinWindowTrades, double MinLift, double PriorStrength, int MinFoldTrades, int MinEvaluableFolds, int HoldOutMonths);

/// <summary>One cell of a stored entry-window study (P8-13b). A window is HH:mm text; a metric that cannot be computed is null. A status other than <c>Proposed</c> is the answer, shown as such.</summary>
public sealed record EntryWindowCellView(
    string CellKey,
    string Status,
    int CellTrades,
    double? CellWinRate,
    string? WindowFrom,
    string? WindowTo,
    int WindowTrades,
    double KeptShare,
    double? ShrunkRate,
    double? Lift,
    double? MeanRInside,
    double? MeanROutside,
    bool RemovesMajority,
    int FoldsEvaluable,
    int FoldsHeld,
    int FoldsRequired,
    double? PooledLift,
    IReadOnlyList<string> Reasons);

/// <summary>A stored entry-window study as the API shows it (<c>schemaVersion</c> 1). Every figure is a server field; the screen computes none.</summary>
/// <param name="SchemaVersion">1.</param>
/// <param name="Id">The row id; null inside the stored JSON.</param>
/// <param name="Outcome"><c>Proposed</c> when any cell has a proposed window, else <c>NoWindowProposed</c>.</param>
/// <param name="Knobs">The knobs.</param>
/// <param name="Rows">Trades given.</param>
/// <param name="From">Earliest trade date; null with no trades.</param>
/// <param name="To">Latest trade date.</param>
/// <param name="UsedFolds">Shared folds that took part.</param>
/// <param name="OutsideSession">Trades outside the regular session (not bucketed).</param>
/// <param name="Cells">One entry per cell.</param>
/// <param name="Limits">Sentences that must be shown with the result.</param>
/// <param name="Basis">gross, costed_unconfirmed or net_confirmed.</param>
/// <param name="Sources">The runs behind the study.</param>
/// <param name="Text">The printed text.</param>
/// <param name="BuiltBy">Who stored it; null inside the stored JSON.</param>
/// <param name="CreatedAt">When; null inside the stored JSON.</param>
public sealed record EntryWindowReportView(
    int SchemaVersion,
    long? Id,
    string Outcome,
    EntryWindowKnobsView Knobs,
    int Rows,
    DateOnly? From,
    DateOnly? To,
    int UsedFolds,
    int OutsideSession,
    IReadOnlyList<EntryWindowCellView> Cells,
    IReadOnlyList<string> Limits,
    string Basis,
    IReadOnlyList<CellSource> Sources,
    string Text,
    string? BuiltBy,
    DateTimeOffset? CreatedAt)
{
    /// <summary>The view version written to <c>report_json</c>.</summary>
    public const int Version = 1;

    internal static EntryWindowReportView Of(EntryWindowReport r, int holdOutMonths, string basis, IReadOnlyList<CellSource> sources, string text)
    {
        var o = r.Options;
        var cells = r.Cells.Select(p => new EntryWindowCellView(
            p.CellKey, p.Status.ToString(), p.CellTrades, Finite(p.CellWinRate),
            p.WindowStartMinute is { } s ? EntryWindowReport.Clock(s) : null, p.WindowEndMinute is { } e ? EntryWindowReport.Clock(e) : null,
            p.WindowTrades, Finite(p.KeptShare) ?? 0, Finite(p.ShrunkRate), Finite(p.Lift), Finite(p.MeanRInside), Finite(p.MeanROutside), p.RemovesMajority,
            p.FoldsEvaluable, p.FoldsHeld, p.FoldsRequired, Finite(p.PooledLift), p.Reasons)).ToList();
        return new EntryWindowReportView(
            Version, null, cells.Any(c => c.Status == nameof(EntryWindowStatus.Proposed)) ? "Proposed" : "NoWindowProposed",
            new EntryWindowKnobsView(o.BucketMinutes, o.MinWindowTrades, o.MinLift, o.PriorStrength, o.MinFoldTrades, o.MinEvaluableFolds, holdOutMonths),
            r.Rows, r.Rows == 0 ? null : r.From, r.Rows == 0 ? null : r.To, r.UsedFolds, r.OutsideSession, cells, r.Limits, basis, sources, text, null, null);
    }

    internal static double? Finite(double? v) => v is { } x && double.IsFinite(x) ? x : null;
}

/// <summary>The knobs a meta-policy evaluation was made with.</summary>
public sealed record MetaPolicyKnobsView(string Split, double MinKeptShare, double MinLiftR, double MinSkillR, double SignificanceZ, int Trials, int HoldOutMonths);

/// <summary>Kept and skipped R of a set of candidates.</summary>
public sealed record MetaTotalsView(int Candidates, int Kept, int Skipped, double? MeanKept, double? MeanSkipped, double BaselinePerCandidate, double PolicyPerCandidate, double Lift, double RandomLift)
{
    internal static MetaTotalsView Of(MetaPolicyTotals t) =>
        new(t.Candidates, t.Kept, t.Skipped, EntryWindowReportView.Finite(t.MeanKept), EntryWindowReportView.Finite(t.MeanSkipped),
            EntryWindowReportView.Finite(t.BaselinePerCandidate) ?? 0, EntryWindowReportView.Finite(t.PolicyPerCandidate) ?? 0, EntryWindowReportView.Finite(t.Lift) ?? 0, EntryWindowReportView.Finite(t.RandomLift) ?? 0);
}

/// <summary>A threshold of one group; a null threshold takes everything.</summary>
public sealed record MetaThresholdView(string Group, double? Threshold, int Rows, double KeptShare);

/// <summary>One fold of a stored policy evaluation.</summary>
public sealed record MetaFoldView(int Index, DateOnly TestStart, DateOnly TestEnd, bool Evaluable, string? Reason, IReadOnlyList<MetaThresholdView> Thresholds, MetaTotalsView Totals);

/// <summary>A stored meta-labelling policy evaluation as the API shows it (<c>schemaVersion</c> 1). <c>NoPolicy</c> is stored like any other result.</summary>
/// <param name="SchemaVersion">1.</param>
/// <param name="Id">The row id; null inside the stored JSON.</param>
/// <param name="Outcome">NoPolicy, NotEnoughEvidence, DoesNotHelp, Inconclusive or Helps.</param>
/// <param name="Reasons">Why, one sentence each.</param>
/// <param name="Knobs">The knobs.</param>
/// <param name="Rows">Development trades given.</param>
/// <param name="From">Earliest trade date; null with no trades.</param>
/// <param name="To">Latest trade date.</param>
/// <param name="UsedFolds">Usable folds.</param>
/// <param name="Folds">Every fold.</param>
/// <param name="Pooled">Kept and skipped pooled over the evaluable folds (out of sample).</param>
/// <param name="Recent">The same over the last evaluated trades.</param>
/// <param name="Policy">The thresholds proposed (chosen in-sample, so optimistic).</param>
/// <param name="Limits">Sentences that must be shown with the result.</param>
/// <param name="Basis">gross, costed_unconfirmed or net_confirmed.</param>
/// <param name="Sources">The runs behind the report.</param>
/// <param name="Text">The printed text.</param>
/// <param name="BuiltBy">Who stored it; null inside the stored JSON.</param>
/// <param name="CreatedAt">When; null inside the stored JSON.</param>
public sealed record MetaPolicyReportView(
    int SchemaVersion,
    long? Id,
    string Outcome,
    IReadOnlyList<string> Reasons,
    MetaPolicyKnobsView Knobs,
    int Rows,
    DateOnly? From,
    DateOnly? To,
    int UsedFolds,
    IReadOnlyList<MetaFoldView> Folds,
    MetaTotalsView Pooled,
    MetaTotalsView Recent,
    IReadOnlyList<MetaThresholdView> Policy,
    IReadOnlyList<string> Limits,
    string Basis,
    IReadOnlyList<CellSource> Sources,
    string Text,
    string? BuiltBy,
    DateTimeOffset? CreatedAt)
{
    /// <summary>The view version written to <c>report_json</c>.</summary>
    public const int Version = 1;

    internal static MetaPolicyReportView Of(MetaPolicyReport r, int holdOutMonths, string basis, IReadOnlyList<CellSource> sources, string text)
    {
        var o = r.Options;
        var folds = r.Folds.Select(f => new MetaFoldView(f.Fold.Index, f.Fold.TestStart, f.Fold.TestEnd, f.Evaluable, f.Reason, [.. f.Thresholds.Select(Threshold)], MetaTotalsView.Of(f.Totals))).ToList();
        return new MetaPolicyReportView(
            Version, null, r.Outcome.ToString(), r.Reasons, new MetaPolicyKnobsView(o.Split.ToString(), o.MinKeptShare, o.MinLiftR, o.MinSkillR, o.SignificanceZ, o.Trials, holdOutMonths),
            r.Rows, r.Rows == 0 ? null : r.From, r.Rows == 0 ? null : r.To, r.UsedFolds, folds, MetaTotalsView.Of(r.Pooled), MetaTotalsView.Of(r.Recent), [.. r.Policy.Select(Threshold)],
            r.Limits, basis, sources, text, null, null);
    }

    private static MetaThresholdView Threshold(MetaPolicyThreshold t) => new(t.Group, EntryWindowReportView.Finite(t.Threshold), t.Rows, EntryWindowReportView.Finite(t.KeptShare) ?? 0);
}
