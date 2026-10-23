using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning;

/// <summary>An experiment as the API shows it (P6-11, ADR 0072). The report text and the parameters are empty in a list and filled for one experiment.</summary>
/// <param name="Id">The ledger id.</param>
/// <param name="ExperimentKey">The hash that identifies the experiment.</param>
/// <param name="KnobName">What was changed.</param>
/// <param name="KnobKind">Parameter, Exit or Slippage.</param>
/// <param name="BaselineValue">The baseline value.</param>
/// <param name="CandidateValue">The candidate value.</param>
/// <param name="BaselineRunKey">The baseline run.</param>
/// <param name="CandidateRunKey">The candidate run.</param>
/// <param name="From">Earliest trade date.</param>
/// <param name="To">Latest trade date.</param>
/// <param name="HoldOutStart">First day of the hold-out that was not read, or null when none was kept.</param>
/// <param name="Outcome">Better, NotBetter, Inconclusive or CheaperAssumption.</param>
/// <param name="Trials">The experiments of the last 30 days when it was judged, itself included.</param>
/// <param name="UsedFolds">Folds used.</param>
/// <param name="FoldsBetter">Folds better.</param>
/// <param name="FoldsNeeded">Folds needed.</param>
/// <param name="RequiredImprovementR">The improvement required, mean R per trade.</param>
/// <param name="BaselineTrades">Baseline trades.</param>
/// <param name="CandidateTrades">Candidate trades.</param>
/// <param name="PooledBaselineMeanR">Pooled mean R of the baseline over the used folds, or null.</param>
/// <param name="PooledCandidateMeanR">The same for the candidate.</param>
/// <param name="BaselineTotalR">Total R of the baseline after the live gap.</param>
/// <param name="CandidateTotalR">The same for the candidate.</param>
/// <param name="ExpectedLiveGapR">The live gap assumed, R per trade.</param>
/// <param name="CompareMs">Milliseconds spent reading both runs and judging them (measured); the replay is not included and not measured.</param>
/// <param name="Reasons">Why it is not better; empty when better.</param>
/// <param name="ReportText">The report with its honest limits; empty in a list.</param>
/// <param name="Parameters">The rules used; empty in a list.</param>
/// <param name="CreatedAt">When it was recorded.</param>
/// <param name="CreatedBy">Who recorded it.</param>
public sealed record ExperimentView(
    long Id,
    string ExperimentKey,
    string KnobName,
    string KnobKind,
    string BaselineValue,
    string CandidateValue,
    string BaselineRunKey,
    string CandidateRunKey,
    DateOnly From,
    DateOnly To,
    DateOnly? HoldOutStart,
    string Outcome,
    int Trials,
    int UsedFolds,
    int FoldsBetter,
    int FoldsNeeded,
    double RequiredImprovementR,
    int BaselineTrades,
    int CandidateTrades,
    double? PooledBaselineMeanR,
    double? PooledCandidateMeanR,
    double BaselineTotalR,
    double CandidateTotalR,
    double ExpectedLiveGapR,
    long CompareMs,
    IReadOnlyList<string> Reasons,
    string ReportText,
    string Parameters,
    DateTimeOffset CreatedAt,
    string CreatedBy)
{
    /// <summary>The view of a stored experiment.</summary>
    internal static ExperimentView Of(StoredExperiment stored)
    {
        var e = stored.Experiment;
        return new ExperimentView(
            stored.Id, e.ExperimentKey, e.Knob.Name, e.Knob.Kind.ToString(), e.Knob.BaselineValue, e.Knob.CandidateValue, e.BaselineRunKey, e.CandidateRunKey, e.FromDate, e.ToDate, e.HoldOutStart, e.Outcome.ToString(), e.Trials, e.UsedFolds,
            e.FoldsBetter, e.FoldsNeeded, e.RequiredImprovementR, e.BaselineTrades, e.CandidateTrades, e.PooledBaselineMeanR, e.PooledCandidateMeanR, e.BaselineTotalR, e.CandidateTotalR, e.ExpectedLiveGapR, e.CompareMs,
            e.Reasons.Length == 0 ? [] : e.Reasons.Split('\n'), e.ReportText, e.Parameters, e.CreatedAt, e.CreatedBy);
    }
}
