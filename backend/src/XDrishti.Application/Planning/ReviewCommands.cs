using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The owner's "approve despite" for one account (spec P7-10, ADR 0095, ADR 0087): the check codes named, and why. It is bound to the account and to the ticket's own symbol by the handler; it covers only the checks it names, on that account.
/// </summary>
/// <param name="AccountId">The account the confirmation is for.</param>
/// <param name="Codes">The check codes approved despite (at least one; never the account gate).</param>
/// <param name="Reason">Why, 1 to 200 characters.</param>
public sealed record CheckConfirmation(Guid AccountId, IReadOnlyList<string> Codes, string Reason);

/// <summary>
/// The owner approves a ticket (spec P7-10). The actor must be an owner actor built from the signed-in session (<c>owner:...</c>), never from a request body. The checklist of every account the ticket is sized for must be complete and
/// without a check that needs confirmation, unless <paramref name="Confirmations"/> names those checks for that account. Approved means watched; no order is placed and none exists.
/// </summary>
/// <param name="PlanId">The plan the ticket belongs to.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="Actor">The owner actor.</param>
/// <param name="Reason">Why, 1 to 500 characters; a default sentence when null.</param>
/// <param name="Confirmations">The checks approved despite, per account; null for none.</param>
public sealed record ApproveTicketCommand(long PlanId, long ItemId, string Actor, string? Reason, IReadOnlyList<CheckConfirmation>? Confirmations) : ICommand;

/// <summary>The owner lowers the size of a ticket for one or more of its accounts (spec P7-10). Only reductions are accepted; the ticket must be approved afterwards, through the same checklist.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="Actor">The owner actor.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Quantities">The new quantities.</param>
public sealed record ModifyTicketCommand(long PlanId, long ItemId, string Actor, string Reason, IReadOnlyList<QuantityChange> Quantities) : ICommand;

/// <summary>The owner rejects a ticket (spec P7-10). Allowed until the cutoff job closes the plan, also after the cutoff time.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="Actor">The owner actor.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
public sealed record RejectTicketCommand(long PlanId, long ItemId, string Actor, string Reason) : ICommand;

/// <summary>The owner approves every primary Proposed ticket whose checklist is complete and has every check passed (spec P7-10). Nothing is approved despite a check; reserve, modified and decided tickets are never touched.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="Actor">The owner actor.</param>
public sealed record ApproveAllCommand(long PlanId, string Actor) : ICommand;

/// <summary>What an owner decision did.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="State">The state after.</param>
/// <param name="Notice">For an approval, <see cref="PlanWording.ApprovedForWatching"/>; otherwise empty.</param>
/// <param name="OverriddenCodes">The checks approved despite, in check order (empty when none).</param>
public sealed record ReviewDecisionOutcome(long ItemId, string State, string Notice, IReadOnlyList<string> OverriddenCodes);

/// <summary>A ticket approve-all left alone, and why.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="Symbol">Its instrument.</param>
/// <param name="Reason">In words, including the check codes that did not pass.</param>
public sealed record SkippedTicket(long ItemId, string Symbol, string Reason);

/// <summary>What approve-all did.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="Approved">The tickets approved.</param>
/// <param name="Skipped">The primary tickets left for the owner, with the reason.</param>
/// <param name="Notice">The approval wording.</param>
public sealed record ApproveAllOutcome(long PlanId, IReadOnlyList<long> Approved, IReadOnlyList<SkippedTicket> Skipped, string Notice);

/// <summary>The review of a plan: each ticket with its checklist per account (spec P7-10). Read only.</summary>
/// <param name="PlanId">The plan.</param>
public sealed record GetPlanReviewQuery(long PlanId) : IQuery<Result<PlanReviewView>>;

/// <summary>The suggestions of a plan with the owner's seen marks (spec P7-10). Read only.</summary>
/// <param name="PlanId">The plan.</param>
public sealed record ListSuggestionsQuery(long PlanId) : IQuery<Result<IReadOnlyList<SuggestionView>>>;

/// <summary>The owner marks a suggestion as seen (spec P7-10). It changes no plan; the mark is an insert-only row.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="SuggestionId">The suggestion.</param>
/// <param name="Actor">The owner actor.</param>
public sealed record MarkSuggestionSeenCommand(long PlanId, long SuggestionId, string Actor) : ICommand;

/// <summary>Runs one pass of the review job (spec P7-10): for each open plan of today's session, the optional automatic pass shortly before the cutoff (only when the owner's setting is ON), and the lapse at and after it.</summary>
public sealed record RunReviewCycleCommand : ICommand;

/// <summary>What a pass of the review job did.</summary>
/// <param name="Plans">Open plans of today's session looked at.</param>
/// <param name="AutoApproved">Tickets approved by the rule (always 0 with the setting OFF).</param>
/// <param name="Lapsed">Tickets that lapsed.</param>
/// <param name="Summary">In words.</param>
public sealed record ReviewCycleOutcome(int Plans, int AutoApproved, int Lapsed, string Summary);
