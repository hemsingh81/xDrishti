using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>
/// Changes the owner's ML switches (spec CF-10 D6 to D8, part 10d-1, ADR 0220): <c>ml.influence.enabled</c>, <c>ml.auto_promote</c> and <c>ml.auto_rollback</c>, all default OFF (no row means OFF). The actor must be <c>owner:...</c>, taken from the signed-in session by the endpoint, never
/// from a body. A value that is null is left alone; a value equal to the one in force writes nothing. Every change appends a row and none is edited. The endpoint asks for a fresh password when the change turns <c>ml.influence.enabled</c> or <c>ml.auto_promote</c> ON.
/// </summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Influence">The new value of <c>ml.influence.enabled</c>, or null to leave it.</param>
/// <param name="AutoPromote">The new value of <c>ml.auto_promote</c>, or null to leave it.</param>
/// <param name="AutoRollback">The new value of <c>ml.auto_rollback</c>, or null to leave it.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="AutoHoldOutScore">The new value of <c>ml.holdout.auto_score</c> (ON by the ADR 0282 seed row), or null to leave it; any change needs a fresh password at the endpoint.</param>
public sealed record ChangeMlSettingsCommand(string Actor, bool? Influence, bool? AutoPromote, bool? AutoRollback, string Reason, bool? AutoHoldOutScore = null) : ICommand;

/// <summary>The switches after a change.</summary>
/// <param name="Switches">The three switches now in force.</param>
/// <param name="Changed">The names of the switches this call changed.</param>
public sealed record MlSettingsChanged(MlSwitches Switches, IReadOnlyList<string> Changed);
