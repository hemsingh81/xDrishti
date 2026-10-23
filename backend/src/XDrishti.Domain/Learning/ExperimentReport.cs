using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>The verdict on one knob change with the figures behind it and the honest limits that ride with it (P6-11, ADR 0072).</summary>
/// <param name="Knob">The knob.</param>
/// <param name="Options">The rules used (the trials as judged).</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="BaselineTrades">Baseline trades in the development part.</param>
/// <param name="CandidateTrades">Candidate trades among them (the same entries; never more than the baseline's).</param>
/// <param name="From">Earliest trade date.</param>
/// <param name="To">Latest trade date.</param>
/// <param name="Folds">Every test window, used or not.</param>
/// <param name="FoldsBetter">Used folds in which the candidate is better by the required improvement.</param>
/// <param name="FoldsNeeded">Used folds it had to win.</param>
/// <param name="RequiredImprovementR">The improvement in mean R per trade required (after the trials).</param>
/// <param name="PooledBaselineMeanR">Mean R of the baseline over the used folds; null without a used fold.</param>
/// <param name="PooledCandidateMeanR">The same for the candidate.</param>
/// <param name="BaselineTotalR">Total R of the baseline after the expected live gap, all development trades.</param>
/// <param name="CandidateTotalR">The same for the candidate.</param>
/// <param name="Reasons">Why it is not better; empty when better.</param>
/// <param name="Limits">Sentences that must be printed with the result.</param>
public sealed record ExperimentReport(
    ExperimentKnob Knob,
    ExperimentOptions Options,
    ExperimentOutcome Outcome,
    int BaselineTrades,
    int CandidateTrades,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ExperimentFold> Folds,
    int FoldsBetter,
    int FoldsNeeded,
    double RequiredImprovementR,
    double? PooledBaselineMeanR,
    double? PooledCandidateMeanR,
    double BaselineTotalR,
    double CandidateTotalR,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Limits)
{
    /// <summary>Used folds.</summary>
    public int UsedFolds => Folds.Count(f => f.Usable);

    /// <summary>Entries of the baseline that the candidate did not take.</summary>
    public int Skipped => BaselineTrades - CandidateTrades;

    /// <summary>A fixed plain-text rendering; the same report gives the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(c, $"experiment {Knob.Name} ({Knob.Kind}): {Knob.BaselineValue} -> {Knob.CandidateValue}: {Outcome}"));
        sb.AppendLine(string.Create(c, $"{BaselineTrades} baseline trades, {CandidateTrades} taken by the candidate ({Skipped} skipped), {From:yyyy-MM-dd} to {To:yyyy-MM-dd}"));
        sb.AppendLine(string.Create(c, $"required improvement {RequiredImprovementR:F4} R per trade (= {Options.BaseImprovementR:F3} + {Options.TrialSlopeR:F3} x sqrt({Options.Trials} trials in {ExperimentOptions.TrialWindowDays} days)), folds better {FoldsBetter} of {UsedFolds}, {FoldsNeeded} needed"));
        foreach (var f in Folds)
        {
            sb.AppendLine(f.Usable
                ? string.Create(c, $"fold {f.Index} {f.TestStart:yyyy-MM-dd}..{f.TestEnd:yyyy-MM-dd}: baseline {f.BaselineTrades} trades mean {f.BaselineMeanR:F4} R, candidate {f.CandidateTrades} trades mean {f.CandidateMeanR:F4} R, difference {f.Difference:F4}{(f.Better ? ", better" : string.Empty)}")
                : string.Create(c, $"fold {f.Index} {f.TestStart:yyyy-MM-dd}..{f.TestEnd:yyyy-MM-dd}: not used, {f.SkipReason}"));
        }

        if (PooledBaselineMeanR is { } b && PooledCandidateMeanR is { } k)
        {
            sb.AppendLine(string.Create(c, $"pooled over the used folds: baseline {b:F4} R, candidate {k:F4} R, difference {k - b:F4}"));
        }

        sb.AppendLine(string.Create(c, $"total R after the expected live gap of {Options.ExpectedLiveGapR:F3} R per trade: baseline {BaselineTotalR:F2}, candidate {CandidateTotalR:F2}, at most {Options.MaxTotalRDrop:P0} may be lost"));
        foreach (var reason in Reasons)
        {
            sb.AppendLine("- " + reason);
        }

        foreach (var limit in Limits)
        {
            sb.AppendLine("limit: " + limit);
        }

        return sb.ToString();
    }
}
