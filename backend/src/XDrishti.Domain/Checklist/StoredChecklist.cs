using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Checklist;

/// <summary>
/// Rebuilds the checklist of a stored ticket from the standings the plan kept (spec P7-10, ADR 0095, ADR 0092 point 5): the plan has no checklist column, only the codes of the checks that were not a pass, spelled
/// <c>checklist:{code}:fail</c> or <c>checklist:{code}:unavailable</c>. Every check that is not named there is a pass of the evaluation that built the plan; a name that is not a check of the list, or an outcome that is neither, means the record cannot be
/// read and there is NO result (a missing checklist never approves). The standings are those of the whole plan (all accounts together), so one account's failure shows on every account: that can only make approval harder, never easier.
/// The account gate is not taken from the standings but from the mode of the allocation itself: a non-paper allocation is refused outright. Pure; builds only <see cref="ChecklistResult"/>s, which can only be reviewed, never approved by themselves.
/// </summary>
public static class StoredChecklist
{
    /// <summary>The prefix of a stored checklist standing.</summary>
    public const string Prefix = "checklist:";

    /// <summary>The result for one account of one ticket, or null when a standing cannot be read.</summary>
    /// <param name="accountId">The account the ticket is sized for.</param>
    /// <param name="symbol">The ticket's instrument.</param>
    /// <param name="paperMode">Whether the allocation is to a paper-mode account.</param>
    /// <param name="standings">The stored list that may hold <c>checklist:...</c> entries; other entries are ignored.</param>
    /// <param name="liveMode">Whether the allocation is to a manual-live account: its first check is the live-ready gate, read from the standings like the others (spec P11-04).</param>
    public static ChecklistResult? Build(string accountId, string symbol, bool paperMode, IEnumerable<string> standings, bool liveMode = false)
    {
        ArgumentNullException.ThrowIfNull(standings);
        if (!paperMode && !liveMode)
        {
            return new ChecklistResult(accountId, symbol, [new ChecklistCheck(ChecklistCodes.AccountPaper, FilterOutcome.Fail, CheckPolicy.BlockWithConfirm, null, null, "refused: the allocation is not to a paper-mode account; only paper-mode Trading accounts receive tickets")]);
        }

        var outcomes = new Dictionary<string, FilterOutcome>(StringComparer.Ordinal);
        foreach (var standing in standings.Where(s => s.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var parts = standing[Prefix.Length..].Split(':');
            if (parts.Length != 2 || !(ChecklistCodes.All.Contains(parts[0], StringComparer.Ordinal) || parts[0] == ChecklistCodes.AccountLiveReady))
            {
                return null;
            }

            FilterOutcome outcome;
            switch (parts[1])
            {
                case "fail":
                    outcome = FilterOutcome.Fail;
                    break;
                case "unavailable":
                    outcome = FilterOutcome.Unavailable;
                    break;
                default:
                    return null;
            }

            // The account gate of the whole plan concerns accounts other than this one (a live account in the book); this account's gate was judged above.
            if (parts[0] != ChecklistCodes.AccountPaper && (liveMode || parts[0] != ChecklistCodes.AccountLiveReady))
            {
                outcomes[parts[0]] = outcomes.TryGetValue(parts[0], out var seen) && seen == FilterOutcome.Unavailable ? FilterOutcome.Unavailable : outcome;
            }
        }

        var policy = ChecklistPolicy.Default;
        var checks = new List<ChecklistCheck>(ChecklistCodes.All.Count);
        foreach (var code in liveMode ? ChecklistCodes.LiveAll : ChecklistCodes.All)
        {
            if (code == ChecklistCodes.AccountPaper)
            {
                checks.Add(new ChecklistCheck(code, FilterOutcome.Pass, CheckPolicy.BlockWithConfirm, null, null, "paper-mode allocation"));
                continue;
            }

            var outcome = outcomes.GetValueOrDefault(code, FilterOutcome.Pass);
            var detail = outcome switch
            {
                FilterOutcome.Pass => "passed when the plan was built",
                FilterOutcome.Fail => "failed when the plan was built",
                _ => "unavailable when the plan was built",
            };
            checks.Add(new ChecklistCheck(code, outcome, policy.Of(code), null, null, detail));
        }

        return new ChecklistResult(accountId, symbol, checks);
    }
}
