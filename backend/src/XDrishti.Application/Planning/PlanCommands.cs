using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>Moves one ticket (P7-03). Approving, modifying and rejecting need an owner actor (<c>owner:...</c>; the first write endpoint must set it from the authenticated identity, never from a request body) and, for approving and modifying, a time before the plan's review cutoff; every other move is a service's. Nothing here reaches a broker.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="To">The state to move to.</param>
/// <param name="Actor">Who: <c>owner:...</c> for the owner, anything else is a service.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Modifications">What was edited, 1 to 1000 characters; required when moving to Modified, refused otherwise.</param>
/// <param name="ConfirmReason">The reason to approve despite a failed check, up to 500 characters; only on Approved.</param>
public sealed record MoveTicketCommand(long ItemId, TicketState To, string Actor, string Reason, string? Modifications = null, string? ConfirmReason = null) : ICommand;

/// <summary>Moves a plan (P7-03). The plan can never say more than its tickets.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="To">The state to move to.</param>
/// <param name="Actor">Who.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
public sealed record MovePlanCommand(long PlanId, PlanState To, string Actor, string Reason) : ICommand;

/// <summary>Applies the review cutoff to a plan: every ticket nobody decided on lapses, and the plan takes the state its tickets support. The cutoff time is the plan's own, derived from the session calendar when the plan was published, never given by the caller; refused before it. It cannot approve anything.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="Actor">The service doing it.</param>
public sealed record ApplyReviewCutoffCommand(long PlanId, string Actor) : ICommand;

/// <summary>What a ticket move did.</summary>
/// <param name="Id">The ticket.</param>
/// <param name="State">The state after.</param>
public sealed record TicketMoveOutcome(long Id, string State);

/// <summary>What a plan move or the cutoff did.</summary>
/// <param name="Id">The plan.</param>
/// <param name="State">The plan state after.</param>
/// <param name="Lapsed">How many tickets lapsed (cutoff only).</param>
public sealed record PlanMoveOutcome(long Id, string State, int Lapsed);
