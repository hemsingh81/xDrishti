namespace XDrishti.Domain.Checklist;

/// <summary>What the review screen shows for a ticket and account (P7-10 reads this): every check with its standing, and whether approval is possible.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Symbol">The ticket's instrument.</param>
/// <param name="Checks">Every check with its standing, in a fixed order.</param>
/// <param name="Confirmation">The owner's confirmation when it was used for at least one check; otherwise null (an unneeded confirmation is not recorded).</param>
public sealed record ChecklistReview(string AccountId, string Symbol, IReadOnlyList<ReviewedCheck> Checks, OwnerConfirmation? Confirmation)
{
    /// <summary>Whether the ticket may be approved: nothing refused and nothing left needing confirmation.</summary>
    public bool CanApprove => Checks.All(c => c.Standing is not (CheckStanding.Refused or CheckStanding.NeedsConfirmation));

    /// <summary>The codes approved despite, in check order.</summary>
    public IReadOnlyList<string> ConfirmedDespiteCodes => Checks.Where(c => c.Standing == CheckStanding.ConfirmedDespite).Select(c => c.Check.Code).ToList();
}
