namespace XDrishti.Api.Endpoints;

/// <summary>The knob of a replay: exactly one thing changed.</summary>
/// <param name="Kind"><c>exit</c> or <c>slippage</c> (a <c>parameter</c> is refused).</param>
/// <param name="Name">The knob name from the preview, for example <c>FIXED_RR.rr</c> or <c>slippage.ticks</c>.</param>
/// <param name="CandidateValue">The new value.</param>
internal sealed record ExperimentKnobRequest(string? Kind, string? Name, double? CandidateValue);

/// <summary>The body of a replay start. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="BaselineRunKey">The stored run to change one thing of (64 lower-case hex).</param>
/// <param name="Knob">The one knob.</param>
/// <param name="HoldOutMonths">Months kept out of the judgement (0 to 120, default 6).</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS start and checked on every request (an earlier step-up window does not count and is not used up).</param>
internal sealed record StartExperimentRequest(string? BaselineRunKey, ExperimentKnobRequest? Knob, int? HoldOutMonths, string? Reason, string? Password);
