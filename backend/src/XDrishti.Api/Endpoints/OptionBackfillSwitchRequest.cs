namespace XDrishti.Api.Endpoints;

/// <summary>The body of the expired-options backfill switch. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Enabled">The new value of the owner's switch: ON allows the backfill to run (when the deployment flag, the probe acceptance and the service are on too), OFF stops it.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS change - ON or OFF. It is checked on every request (a step-up window opened earlier does not count and is not used up or extended); wrong ones count toward the sign-in lockout.</param>
internal sealed record OptionBackfillSwitchRequest(bool Enabled, string? Reason, string? Password);

/// <summary>The body of the acceptance of the probe result. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Note">What was read and where it is recorded (ADR 0177), 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS act. It is checked on every request.</param>
internal sealed record OptionBackfillProbeRequest(string? Note, string? Password);
