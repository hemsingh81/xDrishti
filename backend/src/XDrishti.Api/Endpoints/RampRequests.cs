namespace XDrishti.Api.Endpoints;

/// <summary>The body of a ramp step move (spec P11-04). The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="ToStep">1 (the first step) or a lower step than now. A raise is refused until the evidence gates exist (409 live.gates_not_met).</param>
/// <param name="Reason">1 to 300 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS move (checked on every request; no step-up window is used).</param>
internal sealed record SetRampStepRequest(Guid AccountId, int ToStep, string? Reason, string? Password);

/// <summary>The body of a change of the live switches (spec P11-04). Both default OFF.</summary>
/// <param name="AutoApproveTickets">live.auto_approve_tickets.</param>
/// <param name="AutoRampUp">live.auto_ramp_up (takes effect with the evidence gates of P11-04b).</param>
/// <param name="Reason">1 to 500 characters.</param>
/// <param name="ExpectedSettingsId">The row the form was loaded at, 0 for none; a different row now is a 409.</param>
/// <param name="Password">The owner's password, re-entered for THIS change.</param>
internal sealed record ChangeLiveSettingsRequest(bool AutoApproveTickets, bool AutoRampUp, string? Reason, long? ExpectedSettingsId, string? Password);
