namespace XDrishti.Domain.Planning;

/// <summary>
/// Whose decision an approval row records (spec P7-03, ADR 0083). Owner is the only member: there is no system, rule or automatic kind, and the table refuses any other value. Adding one is the owner's decision (P7-00 questions 3 and 4)
/// and a new migration, not a code path that appears by itself.
/// </summary>
public enum ApprovalKind
{
    /// <summary>The owner decided.</summary>
    Owner,
}

/// <summary>What the owner decided on a ticket.</summary>
public enum ApprovalDecision
{
    /// <summary>Approve as proposed.</summary>
    Approve,

    /// <summary>Edit size, stop or account; the edit is recorded.</summary>
    Modify,

    /// <summary>Reject.</summary>
    Reject,
}

/// <summary>Maps an owner decision to the ticket state it leads to.</summary>
public static class ApprovalDecisionExtensions
{
    /// <summary>The state the decision moves a ticket to.</summary>
    public static TicketState Target(this ApprovalDecision decision) => decision switch
    {
        ApprovalDecision.Approve => TicketState.Approved,
        ApprovalDecision.Modify => TicketState.Modified,
        ApprovalDecision.Reject => TicketState.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(decision)),
    };

    /// <summary>The lower-case code stored.</summary>
    public static string Code(this ApprovalDecision decision) => decision.ToString().ToLowerInvariant();
}
