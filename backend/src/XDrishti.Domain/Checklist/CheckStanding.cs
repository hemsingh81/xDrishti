namespace XDrishti.Domain.Checklist;

/// <summary>Where a check stands for approval, after the owner's confirmation (if any) is taken into account.</summary>
public enum CheckStanding
{
    /// <summary>The check passed.</summary>
    Passed,

    /// <summary>Failed, policy warn: shown, approval needs nothing more.</summary>
    Warned,

    /// <summary>Failed or unavailable, and not confirmed: approval is not possible.</summary>
    NeedsConfirmation,

    /// <summary>Failed or unavailable, and the owner approved despite it with a recorded reason. Never shown as a pass.</summary>
    ConfirmedDespite,

    /// <summary>The account may not receive tickets at all (not a paper-mode Trading account). Nothing can override it.</summary>
    Refused,
}
