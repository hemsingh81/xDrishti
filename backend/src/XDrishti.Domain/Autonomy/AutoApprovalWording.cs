namespace XDrishti.Domain.Autonomy;

/// <summary>The fixed names and sentences of an auto approval (spec P7-12, ADR 0091). A test pins them.</summary>
public static class AutoApprovalWording
{
    /// <summary>The actor of an auto approval and of its ticket move. Never an <c>owner:</c> prefix: the database refuses that for kind auto.</summary>
    public const string Actor = "rule:autonomy";

    /// <summary>The kind of the approval row.</summary>
    public const string Kind = "auto";

    /// <summary>The version of the rule; stored with every auto approval. Change it when the rule changes.</summary>
    public const string PolicyVersion = "auto-approval/1";

    /// <summary>The start of every auto-approval reason, notification and Decisions line.</summary>
    public const string Marker = "auto-approved by rule, no order exists";
}
