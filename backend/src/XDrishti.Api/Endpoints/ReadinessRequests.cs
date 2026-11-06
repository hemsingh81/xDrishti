namespace XDrishti.Api.Endpoints;

/// <summary>The body of the readiness enforce switch. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Enforce">The new value of <c>confidence.readiness.enforce</c> (default OFF): ON makes a failed readiness gate refuse the lifecycle move it judges (Candidate to Paper, the re-enable of a paused cell, Paper to Active); OFF only warns.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS change, ON or OFF (turning it OFF removes a refusal). It is checked on every request; wrong ones count toward the sign-in lockout; missing: 403 auth.step_up_required.</param>
internal sealed record ReadinessSwitchRequest(bool Enforce, string? Reason, string? Password);
