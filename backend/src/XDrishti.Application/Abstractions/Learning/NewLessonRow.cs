namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A row to write to the lesson log. <see cref="LessonId"/> and <see cref="FromState"/> are null for the opening row (its own id is then the lesson id).</summary>
/// <param name="LessonId">The opening row's id, or null for an opening.</param>
/// <param name="State">The state after the row.</param>
/// <param name="FromState">The state before, null for an opening.</param>
/// <param name="MonitorKind">The monitor code (<c>growing_tag</c>, <c>rolling_band</c>, <c>cusum</c>, <c>calibration</c>).</param>
/// <param name="Subject">The cell key or <c>model:ID</c>.</param>
/// <param name="EvidenceKey">Identifies the finding; with the monitor and subject it makes an opening idempotent.</param>
/// <param name="Problem">The problem with its numbers (up to 500 characters).</param>
/// <param name="HypothesisRef">The linked hypothesis, or null.</param>
/// <param name="ExperimentId">The linked or resolved experiment, or null.</param>
/// <param name="Result"><c>improved</c> or <c>no_measurable_change</c>, only on a closing row.</param>
/// <param name="ExperimentOutcome">The outcome of the experiment the result was read from.</param>
/// <param name="BeforeMeanR">Pooled mean R of the baseline in that experiment; null when it had no usable fold.</param>
/// <param name="AfterMeanR">The same for the candidate.</param>
/// <param name="BeforeTotalR">Total R of the baseline in that experiment.</param>
/// <param name="AfterTotalR">The same for the candidate.</param>
/// <param name="Reason">Why the row was written.</param>
/// <param name="Actor">Who wrote it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record NewLessonRow(
    long? LessonId,
    string State,
    string? FromState,
    string MonitorKind,
    string Subject,
    string EvidenceKey,
    string Problem,
    long? HypothesisRef,
    long? ExperimentId,
    string? Result,
    string? ExperimentOutcome,
    double? BeforeMeanR,
    double? AfterMeanR,
    double? BeforeTotalR,
    double? AfterTotalR,
    string Reason,
    string Actor,
    DateTimeOffset CreatedAt);
