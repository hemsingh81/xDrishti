namespace XDrishti.Domain.Checklist;

/// <summary>What the review screen shows for a ticket and account (P7-10 reads this): every check with its standing, and whether approval is possible. Only <see cref="ChecklistResult.Review"/> builds one.</summary>
public sealed record ChecklistReview
{
    internal ChecklistReview(string accountId, string symbol, IReadOnlyList<ReviewedCheck> checks, OwnerConfirmation? confirmation)
    {
        AccountId = accountId;
        Symbol = symbol;
        Checks = checks;
        Confirmation = confirmation;
    }

    /// <summary>The account.</summary>
    public string AccountId { get; }

    /// <summary>The ticket's instrument.</summary>
    public string Symbol { get; }

    /// <summary>Every check with its standing, in a fixed order.</summary>
    public IReadOnlyList<ReviewedCheck> Checks { get; }

    /// <summary>The owner's confirmation when it was used for at least one check; otherwise null (an unneeded confirmation is not recorded).</summary>
    public OwnerConfirmation? Confirmation { get; }

    /// <summary>Whether the ticket may be approved: every expected check is present exactly once in the fixed order (the account gate first), none is refused and none still needs confirmation. An empty or partial list never approves.</summary>
    public bool CanApprove =>
        Checks.Select(c => c.Check.Code).SequenceEqual(ChecklistCodes.All)
        && Checks.All(c => c.Standing is not (CheckStanding.Refused or CheckStanding.NeedsConfirmation));

    /// <summary>The codes approved despite, in check order.</summary>
    public IReadOnlyList<string> ConfirmedDespiteCodes => Checks.Where(c => c.Standing == CheckStanding.ConfirmedDespite).Select(c => c.Check.Code).ToList();

    /// <summary>The text to store with the approval (<c>confirm_reason</c>): the overridden codes and the reason; null when nothing was overridden.</summary>
    public string? ConfirmationText => Confirmation is { } c && ConfirmedDespiteCodes.Count > 0 ? c.StoredText(ConfirmedDespiteCodes) : null;
}
