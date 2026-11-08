using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Switches the shadow record ON or OFF as the owner (spec CF-08 part B, D7, ADR 0212): the actor must be <c>owner:...</c>, from the authenticated identity after a fresh password (the endpoint), never from a body. Every call appends a row; none is edited. ON only allows
/// the services <c>shadow-write</c> and <c>shadow-grade</c> (which also start paused); it moves no ticket and places no order. No auto option exists: no approval is involved.
/// </summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Enabled">The new value of <c>paper.shadow.enabled</c>.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="AcknowledgeGap">Turning ON while more trading sessions than <see cref="ShadowOptions.MaxResumeGapSessions"/> have opened since the chain's newest session is refused (409 <c>paper.shadow_gap</c>) unless the owner accepts that each becomes a gap row, never back-filled (ADR 0232).</param>
public sealed record ChangeShadowSwitchCommand(string Actor, bool Enabled, string Reason, bool AcknowledgeGap = false) : ICommand;

/// <summary>The answer of a switch change: the row and the new value.</summary>
/// <param name="Id">The row appended.</param>
/// <param name="Enabled">The value now in force.</param>
public sealed record ShadowSwitched(long Id, bool Enabled);
