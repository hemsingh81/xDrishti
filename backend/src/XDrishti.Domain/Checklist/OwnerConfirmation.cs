using XDrishti.Domain.Common;

namespace XDrishti.Domain.Checklist;

/// <summary>The owner's "approve despite" (spec P7-05): who, and why. Only the owner can make one, and the reason cannot be blank. It is recorded with the approval (<c>approvals.confirmation_reason</c>); it never turns a check into a pass.</summary>
public sealed record OwnerConfirmation
{
    /// <summary>The prefix of an owner actor (as in ADR 0083).</summary>
    public const string OwnerPrefix = "owner:";

    /// <summary>The longest reason accepted.</summary>
    public const int MaxReasonLength = 500;

    private OwnerConfirmation(string actor, string reason)
    {
        Actor = actor;
        Reason = reason;
    }

    /// <summary>Who confirmed.</summary>
    public string Actor { get; }

    /// <summary>Why, trimmed.</summary>
    public string Reason { get; }

    /// <summary>Makes a confirmation, or says why not: the actor must be <c>owner:</c> plus a name, the reason must have text and fit.</summary>
    public static Result<OwnerConfirmation> Create(string? actor, string? reason)
    {
        if (actor is null || !actor.StartsWith(OwnerPrefix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(actor[OwnerPrefix.Length..]))
        {
            return Result.Failure<OwnerConfirmation>(Error.Forbidden("checklist.owner_only", "Only the owner can approve despite a check."));
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Result.Failure<OwnerConfirmation>(Error.Validation("checklist.reason_required", "Approving despite a check needs a reason."));
        }

        if (text.Length > MaxReasonLength)
        {
            return Result.Failure<OwnerConfirmation>(Error.Validation("checklist.reason_too_long", "The reason is at most " + MaxReasonLength + " characters."));
        }

        return Result.Success(new OwnerConfirmation(actor, text));
    }
}
