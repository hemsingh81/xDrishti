using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Confidence;

/// <summary>
/// Switches <c>confidence.kill.auto</c> ON or OFF as the owner (spec CF-12 D3, part 12b, ADR 0219): the actor must be <c>owner:...</c>, from the authenticated identity after a fresh password (the endpoint), never from a body. Every call appends a row; none is edited. ON only
/// lets the daily decay-monitor demote on a firing rule (never on the CF-09 failure boundary); it moves nothing itself and places no order. No row means OFF.
/// </summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Auto">The new value of <c>confidence.kill.auto</c>.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
public sealed record ChangeKillSwitchCommand(string Actor, bool Auto, string Reason) : ICommand;

/// <summary>The answer of a switch change: the row and the new value.</summary>
/// <param name="Id">The row appended.</param>
/// <param name="Auto">The value now in force.</param>
public sealed record KillSwitched(long Id, bool Auto);
