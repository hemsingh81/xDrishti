using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>An experiment as it is kept in <c>research.experiments</c> (P6-11, ADR 0072): the identity, the knob, the two runs, the numbers that decided and the text. Never changed after it is written.</summary>
/// <param name="ExperimentKey">SHA-256 (64 lower-case hex) of the canonical text of the experiment's inputs: the idempotency key.</param>
/// <param name="Knob">The knob.</param>
/// <param name="BaselineRunKey">The stored baseline run.</param>
/// <param name="CandidateRunKey">The stored candidate run.</param>
/// <param name="FromDate">Earliest trade date.</param>
/// <param name="ToDate">Latest trade date.</param>
/// <param name="HoldOutStart">First day of the hold-out that was not read, or null when none was kept.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Trials">The trials judged with: the experiments of the last 30 days, this one included.</param>
/// <param name="UsedFolds">Folds used.</param>
/// <param name="FoldsBetter">Folds better.</param>
/// <param name="FoldsNeeded">Folds needed.</param>
/// <param name="RequiredImprovementR">The improvement required, R per trade.</param>
/// <param name="BaselineTrades">Baseline trades.</param>
/// <param name="CandidateTrades">Candidate trades.</param>
/// <param name="PooledBaselineMeanR">Pooled mean R of the baseline over the used folds; null without one.</param>
/// <param name="PooledCandidateMeanR">The same for the candidate.</param>
/// <param name="BaselineTotalR">Total R of the baseline after the live gap.</param>
/// <param name="CandidateTotalR">The same for the candidate.</param>
/// <param name="ExpectedLiveGapR">The live gap assumed, R per trade.</param>
/// <param name="CompareMs">Milliseconds spent reading both runs and judging them (measured). The replay that made the candidate run is not included and is not measured.</param>
/// <param name="Reasons">Why it is not better, one per line; empty when better.</param>
/// <param name="ReportText">The report with its limits and the timing note.</param>
/// <param name="Parameters">The rules used, as canonical text.</param>
/// <param name="CreatedAt">When it was recorded.</param>
/// <param name="CreatedBy">Who recorded it.</param>
public sealed record ExperimentToStore(
    string ExperimentKey,
    ExperimentKnob Knob,
    string BaselineRunKey,
    string CandidateRunKey,
    DateOnly FromDate,
    DateOnly ToDate,
    DateOnly? HoldOutStart,
    ExperimentOutcome Outcome,
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
    string Reasons,
    string ReportText,
    string Parameters,
    DateTimeOffset CreatedAt,
    string CreatedBy);
