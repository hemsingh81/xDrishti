namespace XDrishti.Domain.Autonomy;

/// <summary>What the auto-approval rule decided (spec P7-12): approve, or decline (leave the ticket for the owner, who keeps the normal manual path). The settings snapshot and the policy version are recorded either way.</summary>
public sealed record AutoApprovalDecision
{
    internal AutoApprovalDecision(bool approved, IReadOnlyList<AutoDecline> reasons, string summary, AutonomySettings settings)
    {
        Approved = approved;
        Reasons = reasons;
        Summary = summary;
        SettingsId = settings.Id;
        SettingsHash = settings.Hash;
    }

    /// <summary>True to approve by rule; false to decline.</summary>
    public bool Approved { get; }

    /// <summary>Why it declined (empty when it approved). Every reason that applied, not only the first.</summary>
    public IReadOnlyList<AutoDecline> Reasons { get; }

    /// <summary>For an approval, the sentence stored as the approval's reason (starts with <see cref="AutoApprovalWording.Marker"/>); for a decline, a one-line list of the codes.</summary>
    public string Summary { get; }

    /// <summary>The settings row the decision rests on.</summary>
    public long? SettingsId { get; }

    /// <summary>The hash of that row.</summary>
    public string? SettingsHash { get; }

    /// <summary>The version of the rule.</summary>
    public string PolicyVersion { get; } = AutoApprovalWording.PolicyVersion;
}
