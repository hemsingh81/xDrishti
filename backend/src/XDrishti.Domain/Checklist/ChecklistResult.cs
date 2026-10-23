using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Checklist;

/// <summary>The checks of one ticket for one account, before any owner confirmation. Only <see cref="PreTradeChecklist.Evaluate"/> builds one, so a result cannot be assembled from a hand-picked list of passes.</summary>
public sealed record ChecklistResult
{
    internal ChecklistResult(string accountId, string symbol, IReadOnlyList<ChecklistCheck> checks)
    {
        AccountId = accountId;
        Symbol = symbol;
        Checks = checks;
    }

    /// <summary>The account.</summary>
    public string AccountId { get; }

    /// <summary>The ticket's instrument.</summary>
    public string Symbol { get; }

    /// <summary>Every check, in a fixed order.</summary>
    public IReadOnlyList<ChecklistCheck> Checks { get; }

    /// <summary>Whether the account may not receive the ticket at all.</summary>
    public bool Refused => Checks.Any(IsRefusal);

    /// <summary>The review with the owner's confirmation applied. The confirmation covers only the checks it names, for its own account and symbol; any other check that needs one stays at needs-confirmation. It never changes an outcome and never covers a refused account.</summary>
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
            else if (confirmation is not null && !Refused && confirmation.Covers(AccountId, Symbol, c.Code))
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
