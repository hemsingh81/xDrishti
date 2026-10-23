namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A row to write. <see cref="HypothesisId"/> is null for the proposing row (its own id is then the hypothesis id) and <see cref="FromState"/> is null there too.</summary>
/// <param name="HypothesisId">The proposing row's id, or null for a proposal.</param>
/// <param name="State">The state after the row.</param>
/// <param name="FromState">The state before, null for a proposal.</param>
/// <param name="Source"><c>owner</c> or <c>grid</c>.</param>
/// <param name="CellKey">The cell the hypothesis is about, or null.</param>
/// <param name="KnobName">The one knob.</param>
/// <param name="KnobKind">Parameter, Exit or Slippage.</param>
/// <param name="BaselineValue">The value in the baseline run.</param>
/// <param name="CandidateValue">The value to try.</param>
/// <param name="Rationale">Why, up to 500 characters.</param>
/// <param name="BaselineRunKey">The stored baseline run.</param>
/// <param name="CandidateRunKey">The candidate run, once attached.</param>
/// <param name="ExperimentId">The experiment that judged it, once judged.</param>
/// <param name="Reason">Why the row was written (the move's reason), up to 500 characters.</param>
/// <param name="Actor">Who wrote it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record NewHypothesisRow(
    long? HypothesisId,
    string State,
    string? FromState,
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
    DateTimeOffset CreatedAt);
