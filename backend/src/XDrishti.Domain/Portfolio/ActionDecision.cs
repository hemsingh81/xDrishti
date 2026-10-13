namespace XDrishti.Domain.Portfolio;

/// <summary>
/// What the owner decided about an Action-center item (ADR 0022): snoozed for a while, done (hidden while the condition holds), or cleared (the decision no longer applies: reopened, or the condition stopped holding).
/// Stored by name (lower case) — never rename a value.
/// </summary>
public enum ActionDecision
{
    Snoozed,
    Done,
    Cleared,
}
