namespace XDrishti.Api.Endpoints;

/// <summary>The body of the automatic kill switch. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Auto">The new value of <c>confidence.kill.auto</c> (default OFF): ON lets the daily decay-monitor demote the cells of a strategy when a kill rule fires, except for the CF-09 failure boundary.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS change, ON or OFF. It is checked on every request (a step-up window opened earlier does not count); wrong ones count toward the sign-in lockout. Missing: 403 auth.step_up_required.</param>
internal sealed record KillSwitchRequest(bool Auto, string? Reason, string? Password);

/// <summary>The body of a re-enable. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Reason">Why, 1 to 300 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS act. Missing: 403 auth.step_up_required; wrong: 401, counted toward the lockout.</param>
internal sealed record ReEnableRequest(string? Reason, string? Password);
