namespace XDrishti.Api.Endpoints;

/// <summary>The body of the shadow-record switch. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Enabled">The new value of <c>paper.shadow.enabled</c>: ON lets the services shadow-write and shadow-grade work (they also start paused), OFF stops them.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS change, ON or OFF. It is checked on every request (a step-up window opened earlier does not count and is not used up or extended); wrong ones count toward the sign-in lockout.</param>
internal sealed record ShadowSwitchRequest(bool Enabled, string? Reason, string? Password);
