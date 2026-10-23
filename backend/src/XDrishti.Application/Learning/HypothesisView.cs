using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>A hypothesis as the API shows it (P6-10, ADR 0076): the newest row of its log, with the legal next states and, for one hypothesis, its history.</summary>
/// <param name="Id">The hypothesis id.</param>
/// <param name="State">proposed, approved, running, judged, accepted, rejected or expired.</param>
/// <param name="Source">owner or grid.</param>
/// <param name="CellKey">The cell it is about, or null.</param>
/// <param name="KnobName">The one knob.</param>
/// <param name="KnobKind">Parameter, Exit or Slippage.</param>
/// <param name="BaselineValue">The value in the baseline run.</param>
/// <param name="CandidateValue">The value to try.</param>
/// <param name="Rationale">Why it was proposed.</param>
/// <param name="BaselineRunKey">The stored baseline run.</param>
/// <param name="CandidateRunKey">The candidate run, once attached.</param>
/// <param name="ExperimentId">The experiment that judged it, once judged.</param>
/// <param name="Reason">The reason of the newest move.</param>
/// <param name="Actor">Who made the newest move.</param>
/// <param name="CreatedAt">When it was proposed.</param>
/// <param name="UpdatedAt">When it last moved.</param>
/// <param name="NextStates">The states it may move to.</param>
/// <param name="History">Every step, oldest first; empty in a list.</param>
/// <param name="Note">What an accepted hypothesis does and does not do.</param>
public sealed record HypothesisView(
    long Id,
    string State,
    string Source,
    string? CellKey,
    string KnobName,
    string KnobKind,
    string BaselineValue,
    string CandidateValue,
    string Rationale,
    string BaselineRunKey,
    string? CandidateRunKey,
    long? ExperimentId,
    string Reason,
    string Actor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> NextStates,
    IReadOnlyList<HypothesisStepView> History,
    string Note)
{
    internal const string AppliedNote = "A hypothesis is a record of a tested idea. Accepting it changes nothing: applying a change is the owner's own act, recorded as a changelog row.";

    internal static HypothesisView Of(HypothesisRow latest, DateTimeOffset createdAt, IReadOnlyList<HypothesisStepView> history)
    {
        var r = latest.Row;
        var state = HypothesisStateText.TryParse(r.State) ?? HypothesisState.Expired;
        return new HypothesisView(latest.HypothesisId, r.State, r.Source, r.CellKey, r.KnobName, r.KnobKind, r.BaselineValue, r.CandidateValue, r.Rationale, r.BaselineRunKey, r.CandidateRunKey, r.ExperimentId, r.Reason, r.Actor, createdAt, r.CreatedAt,
            [.. HypothesisLifecycle.Next(state).Select(s => s.Code())], history, AppliedNote);
    }
}
