namespace XDrishti.Domain.Autonomy;

/// <summary>The codes of <see cref="AutoDecline"/>.</summary>
public static class AutoDeclineCodes
{
    /// <summary>The switch is OFF: manual approval.</summary>
    public const string SettingOff = "setting_off";

    /// <summary>The settings have no stored row (the defaults): nothing to record the decision against.</summary>
    public const string SettingsNotStored = "settings_not_stored";

    /// <summary>The ticket is not primary.</summary>
    public const string NotPrimary = "not_primary";

    /// <summary>The ticket is not in the proposed state (the owner touched it or it was decided).</summary>
    public const string NotProposed = "not_proposed";

    /// <summary>The review cutoff has passed.</summary>
    public const string AfterCutoff = "after_cutoff";

    /// <summary>No lower-bound P(win) is known.</summary>
    public const string PWinUnavailable = "p_win_unavailable";

    /// <summary>The lower-bound P(win) is under the threshold.</summary>
    public const string PWinBelowThreshold = "p_win_below_threshold";

    /// <summary>The ticket lists inputs the plan could not use.</summary>
    public const string InputsUnavailable = "inputs_unavailable";

    /// <summary>The ticket has no account to check.</summary>
    public const string NoAccount = "no_account";

    /// <summary>No checklist for an account of the ticket, or one that is not the full list for this ticket.</summary>
    public const string ChecklistMissing = "checklist_missing";

    /// <summary>A check is unavailable (prefix; the check code follows after a colon).</summary>
    public const string CheckUnavailable = "check_unavailable";

    /// <summary>A check did not pass (prefix; the check code follows after a colon).</summary>
    public const string CheckFailed = "check_failed";

    /// <summary>The ticket has a live allocation and <c>live.auto_approve_tickets</c> is OFF or has no stored row (spec P11-04): the owner approves by hand.</summary>
    public const string AutoLiveOff = "auto_live_off";

    /// <summary>The daily cap is reached.</summary>
    public const string CapReached = "cap_reached";
}
