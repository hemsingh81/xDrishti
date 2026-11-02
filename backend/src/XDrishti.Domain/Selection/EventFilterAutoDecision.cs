namespace XDrishti.Domain.Selection;

/// <summary>What <see cref="EventFilterAutoDecision.Decide"/> concluded: the action, a closed reason code, and the verdict reference the row must carry for an action.</summary>
/// <param name="Action">None, turn on, turn off.</param>
/// <param name="Reason">A closed code, for the log and the answer: <c>approval_manual</c>, <c>verdict_unavailable</c>, <c>verdict_stale</c>, <c>verdict_not_keep</c>, <c>keep_without_evidence</c>, <c>keep_not_confirmed</c>, <c>verdict_window_mismatch</c>, <c>already_on</c>, <c>owner_off</c>, <c>owner_enabled</c>, <c>already_off</c>, <c>keep</c>, <c>not_keep</c>.</param>
/// <param name="VerdictReference">The reference of the verdict that decided (never null for an action).</param>
public sealed record EventFilterAutoDecisionResult(EventFilterAutoAction Action, string Reason, string? VerdictReference);

/// <summary>
/// The auto option of the event-risk filter (CLAUDE.md "approvals have an auto option"; P15-06, ADR 0182), pure. With <c>approval = manual</c> nothing happens here. With <c>approval = auto</c> the filter is turned ON only by a stored protocol-F verdict KEEP that the hold-out confirmed (<see cref="EventFilterVerdict.HoldOutConfirmed"/>), for the
/// <b>current</b> rule version, with a reference to its evidence; any other state, a verdict of another rule version, <c>unavailable</c> and no verdict change nothing. An automatically enabled filter is turned OFF when the newest verdict is an explicit non-KEEP
/// verdict of the current rule version; a filter the owner switched off (an owner OFF row directly after an ON row) is not turned on again here until the owner turns it on or sets approval manual (<c>owner_off</c>), a filter turned on by the owner is never turned off here, and an unavailable verdict never turns anything off or on. The rule never changes N or the approval setting (those are owner acts).
/// </summary>
public static class EventFilterAutoDecision
{
    /// <summary>The decision for the newest verdict (null: none, or unavailable) against the state in force and the current <c>evf_version</c>.</summary>
    public static EventFilterAutoDecisionResult Decide(EventFilterVerdict? verdict, EventFilterState current, string currentRuleVersion)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentRuleVersion);
        if (current.Approval != EventFilterState.AutoApproval)
        {
            return None("approval_manual");
        }

        if (verdict is null || string.IsNullOrWhiteSpace(verdict.State))
        {
            return None("verdict_unavailable");
        }

        if (!string.Equals(verdict.RuleVersion, currentRuleVersion, StringComparison.Ordinal))
        {
            return None("verdict_stale");
        }

        var knownState = verdict.State is EventFilterVerdict.Keep or EventFilterVerdict.Drop or EventFilterVerdict.Inconclusive or EventFilterVerdict.NotEvaluable;
        if (!knownState)
        {
            return None("verdict_unavailable");
        }

        if (verdict.State == EventFilterVerdict.Keep)
        {
            if (string.IsNullOrWhiteSpace(verdict.Reference))
            {
                return None("keep_without_evidence");
            }

            if (verdict.ResultsWithinDays != current.ResultsWithinDays)
            {
                return None("verdict_window_mismatch");
            }

            // Amendment (owner, P15-10): a development KEEP alone never turns the filter on; the one reading of the reserved seasons must have confirmed it, for this population.
            if (!verdict.HoldOutConfirmed)
            {
                return None("keep_not_confirmed");
            }

            if (current.Enabled)
            {
                return None("already_on");
            }

            return current.OwnerSwitchedOff ? None("owner_off") : new EventFilterAutoDecisionResult(EventFilterAutoAction.TurnOn, "keep", verdict.Reference);
        }

        // An explicit verdict that is not KEEP: evidence no longer supports an automatically enabled filter.
        if (!current.Enabled)
        {
            return None("already_off");
        }

        if (current.DecidedBy != EventFilterState.AutoDecision)
        {
            return None("owner_enabled");
        }

        return string.IsNullOrWhiteSpace(verdict.Reference) ? None("verdict_unavailable") : new EventFilterAutoDecisionResult(EventFilterAutoAction.TurnOff, "not_keep", verdict.Reference);
    }

    private static EventFilterAutoDecisionResult None(string reason) => new(EventFilterAutoAction.None, reason, null);
}
