namespace XDrishti.Domain.Planning;

/// <summary>
/// Whose decision an OWNER approval row records (spec P7-03, ADR 0083). Owner is the only member of this enum on purpose: the owner's moves (<see cref="Planning.MoveActor.Owner"/>) are the only ones that carry it. The automatic kind of P7-12 (ADR 0091) is not
/// an owner decision and is written only by its own store method with the actor <c>rule:autonomy</c>, the settings snapshot and the policy version; the database allows it only under the conditions of that ADR. Never add a member here for it.
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
