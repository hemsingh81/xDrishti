using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Replaces the evidence set of the current edge epoch as the owner (spec AM-01 GAP-09, ADR 0282): allowed only before the epoch's first shadow write, never after. The actor must be <c>owner:...</c>, from the authenticated identity after a fresh password (the endpoint),
/// never from a body. It places no order and moves no ticket; it stores one row and registers the versions in the trial ledger (a registration is not a look).
/// </summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Versions">The strategy version hashes of the new set.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
public sealed record ReplaceEvidenceSetCommand(string Actor, IReadOnlyList<string> Versions, string Reason) : ICommand;

/// <summary>The answer of a replace: the set now in force.</summary>
/// <param name="EdgeEpoch">The paper epoch, or null while none exists.</param>
/// <param name="SetHash">The identity of the new set.</param>
/// <param name="Versions">How many versions it holds.</param>
public sealed record EvidenceSetReplaced(long? EdgeEpoch, string SetHash, int Versions);
