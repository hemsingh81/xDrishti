using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Autonomy;

/// <summary>Reads the autonomy settings and their history.</summary>
public sealed record GetAutonomySettingsQuery : IQuery<AutonomySettingsView>;

/// <summary>
/// Changes the autonomy settings (P7-12): the owner's act only. The actor must be <c>owner:...</c> and must come from the authenticated identity (the endpoint needs a recent password re-entry), never from a request body; a service actor is refused.
/// Every call appends a row; none is edited.
/// </summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="AutoApproveTickets">The new value of <c>autonomy.auto_approve_tickets</c>.</param>
/// <param name="PromoteFromReserve">The new value of <c>autonomy.promote_from_reserve</c>.</param>
/// <param name="MinPWin">The least lower-bound P(win), 0.5 to 0.99.</param>
/// <param name="MaxAutoPerDay">The most auto approvals per session, 1 to 20.</param>
/// <param name="ExpectedSettingsId">Optional (the form always sends it): the row the form was loaded at, 0 when nothing was stored. When it is not the row in force now nothing is written (<c>autonomy.stale</c>); null reads the current row at submit, as before (P8-19).</param>
public sealed record ChangeAutonomySettingsCommand(string Actor, string Reason, bool AutoApproveTickets, bool PromoteFromReserve, double MinPWin, int MaxAutoPerDay, long? ExpectedSettingsId = null) : ICommand;

/// <summary>
/// Runs the auto-approval rule over the primary Proposed tickets of a plan, before its review cutoff (P7-12). With the switch OFF it does nothing at all. It has no actor argument: the actor is always the rule. It never moves a plan and never
/// reaches a broker; leaving a ticket undecided keeps the normal path (the owner, or lapse at the cutoff).
/// </summary>
/// <param name="PlanId">The plan.</param>
public sealed record AutoApprovePlanCommand(long PlanId) : ICommand;

/// <summary>The settings in force, with the history newest first.</summary>
/// <param name="Id">The row in force; null while nothing was ever stored (defaults, all OFF).</param>
/// <param name="Hash">Its hash.</param>
/// <param name="AutoApproveTickets"><c>autonomy.auto_approve_tickets</c>.</param>
/// <param name="PromoteFromReserve"><c>autonomy.promote_from_reserve</c>.</param>
/// <param name="MinPWin">The threshold.</param>
/// <param name="MaxAutoPerDay">The daily cap.</param>
/// <param name="Notice">No order is placed in any setting.</param>
/// <param name="History">The changes, newest first (the latest 50).</param>
public sealed record AutonomySettingsView(long? Id, string? Hash, bool AutoApproveTickets, bool PromoteFromReserve, double MinPWin, int MaxAutoPerDay, string Notice, IReadOnlyList<AutonomyChangeView> History);

/// <summary>One change in the history.</summary>
/// <param name="Id">The row.</param>
/// <param name="Hash">The hash.</param>
/// <param name="AutoApproveTickets">The value after.</param>
/// <param name="PromoteFromReserve">The value after.</param>
/// <param name="MinPWin">The threshold after.</param>
/// <param name="MaxAutoPerDay">The cap after.</param>
/// <param name="Actor">Who.</param>
/// <param name="Reason">Why.</param>
/// <param name="ChangedAt">When.</param>
public sealed record AutonomyChangeView(long Id, string Hash, bool AutoApproveTickets, bool PromoteFromReserve, double MinPWin, int MaxAutoPerDay, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>What a pass of the rule did.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="SwitchOn">False when the switch is off and nothing was read or written.</param>
/// <param name="Approved">Tickets approved by rule.</param>
/// <param name="Declined">Tickets left for the owner, with the codes of the reasons.</param>
public sealed record AutoApprovalOutcome(long PlanId, bool SwitchOn, int Approved, IReadOnlyList<AutoDeclinedTicket> Declined);

/// <summary>A ticket the rule left for the owner.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="Codes">The decline codes.</param>
public sealed record AutoDeclinedTicket(long ItemId, IReadOnlyList<string> Codes);
