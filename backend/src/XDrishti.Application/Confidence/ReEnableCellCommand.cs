using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Confidence;

/// <summary>
/// Re-enables a paused cell (<c>Paused</c> to <c>Paper</c>) as the owner (spec CF-12 D3, part 12b, ADR 0219). It raises risk, so it is always an owner act: the actor is <c>owner:...</c> from the signed-in identity after a fresh password (the endpoint), and no automatic path
/// can build this command (an architecture test).
/// </summary>
/// <param name="Cell">The cell key.</param>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Reason">Why, 1 to 300 characters.</param>
public sealed record ReEnableCellCommand(string Cell, string Actor, string Reason) : ICommand;

/// <summary>What a re-enable did.</summary>
/// <param name="Cell">The cell.</param>
/// <param name="ChangelogId">The changelog row written.</param>
/// <param name="From">The state before (<c>paused</c>).</param>
/// <param name="To">The state after (<c>paper</c>).</param>
/// <param name="Warnings">What the owner must know: the CF-11 gates are not enforcing yet, and whether any kill condition was there to clear.</param>
public sealed record ReEnableOutcome(string Cell, long ChangelogId, string From, string To, IReadOnlyList<string> Warnings);
