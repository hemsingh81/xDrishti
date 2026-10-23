using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Checklist;

/// <summary>The checks of one ticket for one account, before any owner confirmation.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Symbol">The ticket's instrument.</param>
/// <param name="Checks">Every check, in a fixed order.</param>
public sealed record ChecklistResult(string AccountId, string Symbol, IReadOnlyList<ChecklistCheck> Checks)
{
    /// <summary>Whether the account may not receive the ticket at all.</summary>
    public bool Refused => Checks.Any(IsRefusal);

    /// <summary>The review with the owner's confirmation applied. A confirmation covers every check that needs one; it never changes an outcome, and it never covers a refused account.</summary>
    public ChecklistReview Review(OwnerConfirmation? confirmation = null)
    {
        var used = false;
        var reviewed = new List<ReviewedCheck>(Checks.Count);
        foreach (var c in Checks)
        {
            CheckStanding s;
            if (IsRefusal(c))
            {
                s = CheckStanding.Refused;
            }
            else if (c.Outcome == FilterOutcome.Pass)
            {
                s = CheckStanding.Passed;
            }
            else if (!c.NeedsConfirmation)
            {
                s = CheckStanding.Warned;
            }
            else if (confirmation is not null && !Refused)
            {
                s = CheckStanding.ConfirmedDespite;
                used = true;
            }
            else
            {
                s = CheckStanding.NeedsConfirmation;
            }

            reviewed.Add(new ReviewedCheck(c, s));
        }

        return new ChecklistReview(AccountId, Symbol, reviewed, used ? confirmation : null);
    }

    private static bool IsRefusal(ChecklistCheck c) => c.Code == ChecklistCodes.AccountPaper && c.Outcome != FilterOutcome.Pass;
}
