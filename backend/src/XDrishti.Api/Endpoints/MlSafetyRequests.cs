namespace XDrishti.Api.Endpoints;

/// <summary>The body of a change of the ML switches. The owner is never part of it: the actor comes from the signed-in session. A switch that is not given is left alone.</summary>
/// <param name="InfluenceEnabled"><c>ml.influence.enabled</c> (default OFF): whether a model may influence a decision at all. ON needs a fresh password; it opens nothing alone (the predicate's other terms must hold too).</param>
/// <param name="AutoPromote"><c>ml.auto_promote</c> (default OFF): whether the daily run may promote a challenger whose promotion rule and predicate terms hold. ON needs a fresh password.</param>
/// <param name="AutoRollback"><c>ml.auto_rollback</c> (default OFF): whether the daily run may execute a rollback proposal. It lowers reliance on ML, so it needs the session and the CSRF token and no password.</param>
/// <param name="AutoHoldOutScore"><c>ml.holdout.auto_score</c> (ON by the ADR 0282 seed row): whether the evening seal service scores the pre-dated champion on the hold-out block. Any change (OFF or back ON) needs a fresh password.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS change. Required when the change turns ml.influence.enabled or ml.auto_promote ON (missing: 403 auth.step_up_required; wrong: 401, counted toward the lockout); ignored otherwise.</param>
internal sealed record MlSettingsRequest(bool? InfluenceEnabled, bool? AutoPromote, bool? AutoRollback, string? Reason, string? Password, bool? AutoHoldOutScore = null);

/// <summary>The body of a promotion. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS act. Missing: 403 auth.step_up_required; wrong: 401, counted toward the lockout.</param>
internal sealed record PromoteModelRequest(string? Reason, string? Password);

/// <summary>The body of the owner's hold-out score of a model. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Password">The owner's password, re-entered for THIS act. Missing: 403 auth.step_up_required; wrong: 401, counted toward the lockout.</param>
internal sealed record MlHoldOutScoreRequest(string? Password);
