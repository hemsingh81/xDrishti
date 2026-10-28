using System.Globalization;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Autonomy;

/// <summary>
/// The auto-approval rule (spec P7-12, ADR 0091), pure. Given a ticket's facts, its checklist results and the settings snapshot it returns Approve or Decline with every reason. Decline leaves the ticket for the owner, who keeps the
/// manual path; at the review cutoff an undecided ticket still lapses. Approve needs ALL of: the switch ON in a stored settings row; a primary ticket still Proposed; before the cutoff; a known lower-bound P(win) at or above the threshold;
/// no unavailable input on the ticket; at least one account and, for each, the complete checklist with EVERY check passed (a warned, failed, unavailable or confirmable-despite check declines, and a confirmation cannot be given because only
/// unconfirmed <see cref="ChecklistResult"/>s are accepted); and the daily cap not reached. Nothing here places an order; there is no order in P7.
/// </summary>
public static class AutoApprovalPolicy
{
    /// <summary>Decides on a primary ticket.</summary>
    public static AutoApprovalDecision Decide(AutoApprovalTicket ticket, IReadOnlyList<ChecklistResult> checklists, AutonomySettings settings, AutoApprovalContext context, LiveSettings? live = null)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(checklists);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        if (!settings.AutoApproveTickets)
        {
            return Decline(settings, [new(AutoDeclineCodes.SettingOff, "automatic approval is switched off: the owner approves by hand")]);
        }

        var reasons = Reasons(ticket, checklists, settings, context, TicketGroup.Primary, checkTiming: true, live);
        if (reasons.Count > 0)
        {
            return Decline(settings, reasons);
        }

        var isLive = ticket.LiveAccountIds is { Count: > 0 };

        var summary = string.Create(CultureInfo.InvariantCulture, $"{AutoApprovalWording.Marker}: P(win) lower bound {ticket.PWinLower:0.###} is at least {settings.MinPWin:0.###}; all {ChecklistCodes.All.Count} checks passed on {ticket.AccountIds.Count} account(s); settings {settings.Id}{(isLive ? $", live settings {live!.Id}" : string.Empty)}, {AutoApprovalWording.PolicyVersion}");
        return new AutoApprovalDecision(true, [], summary, settings, isLive ? live : null);
    }

    /// <summary>The reasons a ticket does not qualify, whatever the switch; empty when it qualifies. Shared with <see cref="ReservePromotion"/>, which adds no weaker test.</summary>
    internal static List<AutoDecline> Reasons(AutoApprovalTicket ticket, IReadOnlyList<ChecklistResult> checklists, AutonomySettings settings, AutoApprovalContext context, TicketGroup group, bool checkTiming, LiveSettings? live = null)
    {
        var reasons = new List<AutoDecline>();
        if (settings.Id is null || string.IsNullOrEmpty(settings.Hash))
        {
            reasons.Add(new(AutoDeclineCodes.SettingsNotStored, "the settings are the defaults, not a stored row"));
        }

        if (ticket.Group != group)
        {
            reasons.Add(new(AutoDeclineCodes.NotPrimary, $"the ticket is {ticket.Group.Code()}, not {group.Code()}"));
        }

        if (checkTiming)
        {
            if (ticket.State != TicketState.Proposed)
            {
                reasons.Add(new(AutoDeclineCodes.NotProposed, $"the ticket is {ticket.State.Code()}, not proposed"));
            }

            if (context.Now >= context.ReviewCutoff)
            {
                reasons.Add(new(AutoDeclineCodes.AfterCutoff, "the review cutoff has passed"));
            }
        }

        if (ticket.PWinLower is not { } p || !double.IsFinite(p) || p is < 0 or > 1)
        {
            reasons.Add(new(AutoDeclineCodes.PWinUnavailable, "no lower-bound P(win) of the shrunk cell estimate is known"));
        }
        else if (p < settings.MinPWin)
        {
            reasons.Add(new(AutoDeclineCodes.PWinBelowThreshold, string.Create(CultureInfo.InvariantCulture, $"P(win) lower bound {p:0.###} is under {settings.MinPWin:0.###}")));
        }

        if (ticket.UnavailableInputs.Count > 0)
        {
            reasons.Add(new(AutoDeclineCodes.InputsUnavailable, "the ticket lists inputs that were unavailable: " + string.Join(", ", ticket.UnavailableInputs)));
        }

        if (ticket.LiveAccountIds is { Count: > 0 } && live is not { CanAutoApprove: true })
        {
            reasons.Add(new(AutoDeclineCodes.AutoLiveOff, "the ticket has a live allocation and live automatic approval is switched off (or has no stored row): the owner approves by hand"));
        }

        AddChecklistReasons(reasons, ticket, checklists);

        if (context.AutoApprovedToday >= settings.MaxAutoPerDay)
        {
            reasons.Add(new(AutoDeclineCodes.CapReached, $"{context.AutoApprovedToday} automatic approvals already today, the cap is {settings.MaxAutoPerDay}"));
        }

        return reasons;
    }

    private static void AddChecklistReasons(List<AutoDecline> reasons, AutoApprovalTicket ticket, IReadOnlyList<ChecklistResult> checklists)
    {
        if (ticket.AccountIds.Count == 0)
        {
            reasons.Add(new(AutoDeclineCodes.NoAccount, "the ticket is sized for no account, so no checklist could be passed"));
            return;
        }

        foreach (var account in ticket.AccountIds.Distinct(StringComparer.Ordinal))
        {
            var mine = checklists.Where(c => c.AccountId == account && string.Equals(c.Symbol, ticket.Symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count != 1 || !mine[0].Checks.Select(c => c.Code).SequenceEqual(ticket.LiveAccountIds?.Contains(account, StringComparer.Ordinal) == true ? ChecklistCodes.LiveAll : ChecklistCodes.All))
            {
                reasons.Add(new(AutoDeclineCodes.ChecklistMissing, $"no complete checklist for account {account}"));
                continue;
            }

            foreach (var check in mine[0].Checks.Where(c => c.Outcome != FilterOutcome.Pass))
            {
                var code = check.Outcome == FilterOutcome.Unavailable ? AutoDeclineCodes.CheckUnavailable : AutoDeclineCodes.CheckFailed;
                reasons.Add(new($"{code}:{check.Code}", $"account {account}: {check.Detail}"));
            }
        }
    }

    private static AutoApprovalDecision Decline(AutonomySettings settings, IReadOnlyList<AutoDecline> reasons) =>
        new(false, reasons, "left for the owner: " + string.Join(", ", reasons.Select(r => r.Code)), settings);
}
