namespace XDrishti.Api.Endpoints;

/// <summary>The body of a ramp step move (spec P11-04). The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="ToStep">1 (the first step) or a lower step than now. A raise by one step is refused with 409 live.gates_not_met unless every evidence gate passes (an unreadable gate is not a pass).</param>
/// <param name="Reason">1 to 300 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS move (checked on every request; no step-up window is used).</param>
internal sealed record SetRampStepRequest(Guid AccountId, int ToStep, string? Reason, string? Password);

/// <summary>The body of a change of the live switches (spec P11-04). Both default OFF.</summary>
/// <param name="AutoApproveTickets">live.auto_approve_tickets.</param>
/// <param name="AutoRampUp">live.auto_ramp_up (lets the evaluator raise S1 to S2 by rule when every gate passes; never above S2).</param>
/// <param name="Reason">1 to 500 characters.</param>
/// <param name="ExpectedSettingsId">The row the form was loaded at, 0 for none; a different row now is a 409.</param>
/// <param name="Password">The owner's password, re-entered for THIS change.</param>
internal sealed record ChangeLiveSettingsRequest(bool AutoApproveTickets, bool AutoRampUp, string? Reason, long? ExpectedSettingsId, string? Password);

/// <summary>The body of clearing a hold or recording the exit confirmation (spec P11-04): the account, a reason and the owner's password for THIS act.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Reason">1 to 300 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS act (checked on every request; no step-up window is used).</param>
internal sealed record RampActRequest(Guid AccountId, string? Reason, string? Password);
