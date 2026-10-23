using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>A lesson as the API shows it (P6-16, ADR 0078): the newest row of its log, with the legal next states and, for one lesson, its history.</summary>
/// <param name="Id">The lesson id.</param>
/// <param name="State">open, linked, closed or dismissed.</param>
/// <param name="MonitorKind">growing_tag, rolling_band, cusum or calibration.</param>
/// <param name="Subject">The cell key or model.</param>
/// <param name="EvidenceKey">Identifies the finding.</param>
/// <param name="Problem">The problem with its numbers.</param>
/// <param name="HypothesisRef">The linked hypothesis, or null.</param>
/// <param name="ExperimentId">The linked or resolved experiment, or null.</param>
/// <param name="Result">improved or no_measurable_change once closed.</param>
/// <param name="ExperimentOutcome">Outcome of the experiment the result was read from.</param>
/// <param name="BeforeMeanR">Pooled mean R before (baseline), or null.</param>
/// <param name="AfterMeanR">Pooled mean R after (candidate), or null.</param>
/// <param name="BeforeTotalR">Total R before.</param>
/// <param name="AfterTotalR">Total R after.</param>
/// <param name="Reason">The reason of the newest move.</param>
/// <param name="Actor">Who made the newest move.</param>
/// <param name="CreatedAt">When it was opened.</param>
/// <param name="UpdatedAt">When it last moved.</param>
/// <param name="NextStates">The states it may move to.</param>
/// <param name="History">Every step, oldest first; empty in a list.</param>
/// <param name="Note">What a lesson does and does not do.</param>
public sealed record LessonView(
    long Id,
    string State,
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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> NextStates,
    IReadOnlyList<LessonStepView> History,
    string Note)
{
    internal const string AppliedNote = "A lesson is a record of a problem found on replayed history, the fix tried and its stored result. Linking or closing it changes nothing: applying a fix is the owner's own act, recorded as a changelog row. Closed with no measurable change is a legitimate result.";

    internal static LessonView Of(LessonRow latest, DateTimeOffset createdAt, IReadOnlyList<LessonStepView> history)
    {
        var r = latest.Row;
        var state = LessonStateText.TryParse(r.State) ?? LessonState.Dismissed;
        return new LessonView(latest.LessonId, r.State, r.MonitorKind, r.Subject, r.EvidenceKey, r.Problem, r.HypothesisRef, r.ExperimentId, r.Result, r.ExperimentOutcome, r.BeforeMeanR, r.AfterMeanR, r.BeforeTotalR, r.AfterTotalR,
            r.Reason, r.Actor, createdAt, r.CreatedAt, [.. LessonLifecycle.Next(state).Select(s => s.Code())], history, AppliedNote);
    }
}
