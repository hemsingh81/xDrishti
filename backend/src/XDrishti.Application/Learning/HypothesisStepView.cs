namespace XDrishti.Application.Learning;

/// <summary>One row of a hypothesis's history.</summary>
/// <param name="RowId">The log row id.</param>
/// <param name="State">The state after the row.</param>
/// <param name="Reason">Why.</param>
/// <param name="Actor">Who.</param>
/// <param name="At">When.</param>
public sealed record HypothesisStepView(long RowId, string State, string Reason, string Actor, DateTimeOffset At);
