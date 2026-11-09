using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Confidence;

/// <summary>
/// Dismisses a kill proposal as the owner (spec CF-12 leftover, ADR 0234): the owner has read it and decided not to pause. The actor is <c>owner:...</c> from the signed-in session, never from a body. It records the decision and the reason and changes no lifecycle state;
/// a dismissed proposal can never be applied afterwards, and the next day's evaluation may propose again.
/// </summary>
/// <param name="ProposalId">The proposal.</param>
/// <param name="Actor">The owner.</param>
/// <param name="Reason">Why, 1 to 400 characters.</param>
public sealed record DismissKillCommand(long ProposalId, string Actor, string Reason) : ICommand;

/// <summary>What dismissing did.</summary>
/// <param name="ProposalId">The proposal.</param>
/// <param name="Strategy">Its strategy.</param>
/// <param name="Rule">Its rule.</param>
/// <param name="State">The state after this call (<c>dismissed</c>).</param>
/// <param name="AlreadyDismissed">True when it was dismissed before this call (nothing was changed now; the first actor and reason stand).</param>
/// <param name="Actor">Who dismissed it (the first time).</param>
/// <param name="Reason">The reason given the first time.</param>
public sealed record DismissKillOutcome(long ProposalId, string Strategy, string Rule, string State, bool AlreadyDismissed, string? Actor, string? Reason);
