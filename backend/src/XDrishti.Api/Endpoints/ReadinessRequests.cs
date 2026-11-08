namespace XDrishti.Api.Endpoints;

/// <summary>The body of the readiness enforce switch. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Enforce">The new value of <c>confidence.readiness.enforce</c> (default OFF): ON makes a failed readiness gate refuse the lifecycle move it judges (Candidate to Paper, the re-enable of a paused cell, Paper to Active); OFF only warns.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS change, ON or OFF (turning it OFF removes a refusal). It is checked on every request; wrong ones count toward the sign-in lockout; missing: 403 auth.step_up_required.</param>
internal sealed record ReadinessSwitchRequest(bool Enforce, string? Reason, string? Password);

/// <summary>The body of the owner's record of a <c>verify-pack --gate</c> result. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="StrategyId">The strategy the pack was run for.</param>
/// <param name="Version">The strategy version.</param>
/// <param name="PackId">The pack folder's id as the gate printed it (1 to 100 characters).</param>
/// <param name="Status">The gate's status in its own spelling: PASS, PASS_WITH_DATA_FINDINGS, RERUN_NEEDED, DATA_REPAIR_NEEDED or BLOCKED_DEFECT. Only PASS over at least 20 trades counts.</param>
/// <param name="TradeCount">The trades the pack held.</param>
/// <param name="Reason">A note, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS record. Missing: 403 auth.step_up_required; wrong: 401 auth.invalid_credentials.</param>
internal sealed record PackGateResultRequest(string? StrategyId, int Version, string? PackId, string? Status, int TradeCount, string? Reason, string? Password);
