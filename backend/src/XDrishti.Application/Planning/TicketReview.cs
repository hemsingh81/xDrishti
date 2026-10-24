using System.Globalization;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The shared steps of the owner's review (spec P7-10, ADR 0095), pure over rows: apply the owner's confirmations to the stored checklist of each account, say what still blocks an approval, build the stored confirmation text,
/// and find the owner's size change of a modified ticket. A confirmation is bound to the account it names and to the ticket's own symbol (never to a symbol the request gives).
/// </summary>
internal static class TicketReview
{
    /// <summary>The longest confirm text the approval row holds.</summary>
    public const int MaxConfirmTextLength = 500;

    public static string Id(Guid accountId) => accountId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>The review per account the ticket is sized for, or a failure when a confirmation is not valid. An account without a result gets an empty review that cannot approve.</summary>
    public static Result<IReadOnlyList<ChecklistReview?>> Review(PlanItemRow item, IReadOnlyList<ChecklistResult> results, IReadOnlyList<CheckConfirmation>? confirmations, string actor)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(results);
        var reviews = new List<ChecklistReview?>(item.Item.Allocations.Count);
        foreach (var allocation in item.Item.Allocations)
        {
            var account = Id(allocation.AccountId);
            var result = results.FirstOrDefault(r => r.AccountId == account && string.Equals(r.Symbol, item.Item.Symbol, StringComparison.OrdinalIgnoreCase));
            var mine = (confirmations ?? []).Where(c => c.AccountId == allocation.AccountId).ToList();
            if (mine.Count > 1)
            {
                return Result.Failure<IReadOnlyList<ChecklistReview?>>(PlanErrors.Invalid([new FieldError("confirmations", "Give one confirmation per account.")]));
            }

            OwnerConfirmation? confirmation = null;
            if (mine.Count == 1)
            {
                var made = OwnerConfirmation.Create(actor, mine[0].Reason, account, item.Item.Symbol, mine[0].Codes);
                if (made.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<ChecklistReview?>>(made.Error);
                }

                confirmation = made.Value;
            }

            reviews.Add(result?.Review(confirmation));
        }

        return Result.Success<IReadOnlyList<ChecklistReview?>>(reviews);
    }

    /// <summary>Why the reviews do not allow approval; null when they do (and there is at least one account).</summary>
    public static string? Blocked(PlanItemRow item, IReadOnlyList<ChecklistReview?> reviews)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
        {
            return "The ticket is sized for no account, so there is no checklist to pass.";
        }

        var problems = new List<string>();
        for (var i = 0; i < reviews.Count; i++)
        {
            var account = Id(item.Item.Allocations[i].AccountId);
            if (reviews[i] is not { } review)
            {
                problems.Add($"account {account}: the plan stored no readable checklist");
            }
            else if (review.Checks.Any(c => c.Standing == CheckStanding.Refused))
            {
                problems.Add($"account {account}: refused ({string.Join(", ", review.Checks.Where(c => c.Standing == CheckStanding.Refused).Select(c => c.Check.Code))}); nothing can override it");
            }
            else if (!review.CanApprove)
            {
                var needs = review.Checks.Where(c => c.Standing == CheckStanding.NeedsConfirmation).Select(c => c.Check.Code).ToList();
                problems.Add(needs.Count > 0 ? $"account {account}: {string.Join(", ", needs)} need the owner's confirmation" : $"account {account}: the checklist is incomplete");
            }
        }

        return problems.Count == 0 ? null : "The checklist does not allow this approval: " + string.Join("; ", problems) + ".";
    }

    /// <summary>The codes that need confirmation, per account, as <c>account:code</c>.</summary>
    public static IReadOnlyList<string> NeedsConfirmation(PlanItemRow item, IReadOnlyList<ChecklistReview?> reviews)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(reviews);
        var list = new List<string>();
        for (var i = 0; i < reviews.Count; i++)
        {
            if (reviews[i] is { } review)
            {
                list.AddRange(review.Checks.Where(c => c.Standing == CheckStanding.NeedsConfirmation).Select(c => Id(item.Item.Allocations[i].AccountId) + ":" + c.Check.Code));
            }
        }

        return list;
    }

    /// <summary>The text for the approval's confirm reason: one entry per account that was overridden; null when nothing was. A failure when it would not fit the column.</summary>
    public static Result<string?> ConfirmText(PlanItemRow item, IReadOnlyList<ChecklistReview?> reviews)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(reviews);
        var parts = new List<string>();
        for (var i = 0; i < reviews.Count; i++)
        {
            if (reviews[i]?.ConfirmationText is { } text)
            {
                parts.Add($"[{Id(item.Item.Allocations[i].AccountId)}] {text}");
            }
        }

        var joined = parts.Count == 0 ? null : string.Join(" | ", parts);
        return joined?.Length > MaxConfirmTextLength
            ? Result.Failure<string?>(PlanErrors.Invalid([new FieldError("confirmations", "The confirmations are too long to store (500 characters); shorten the reasons.")]))
            : Result.Success(joined);
    }

    /// <summary>The codes approved despite, over all accounts, in check order and distinct.</summary>
    public static IReadOnlyList<string> Overridden(IReadOnlyList<ChecklistReview?> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        var codes = reviews.Where(r => r is not null).SelectMany(r => r!.ConfirmedDespiteCodes).ToHashSet(StringComparer.Ordinal);
        return [.. ChecklistCodes.All.Where(codes.Contains)];
    }

    /// <summary>The owner's size change of the ticket (the newest modify row), or null when none; a failure when the stored text no longer holds against the allocations.</summary>
    public static Result<IReadOnlyList<QuantityChange>?> Modification(PlanItemRow item, IReadOnlyList<ApprovalRow> approvals)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(approvals);
        var latest = approvals.Where(a => a.ItemId == item.Id && a.Kind == "owner" && a.Decision == "modify").OrderByDescending(a => a.Id).FirstOrDefault();
        if (latest is null)
        {
            return Result.Success<IReadOnlyList<QuantityChange>?>(null);
        }

        var parsed = TicketModification.Parse(latest.Modifications);
        if (parsed.IsFailure)
        {
            return Result.Failure<IReadOnlyList<QuantityChange>?>(parsed.Error);
        }

        var valid = TicketModification.Validate([.. item.Item.Allocations.Select(a => (a.AccountId, a.Mode, a.Quantity))], parsed.Value);
        return valid.IsFailure ? Result.Failure<IReadOnlyList<QuantityChange>?>(valid.Error) : Result.Success<IReadOnlyList<QuantityChange>?>(valid.Value);
    }

    /// <summary>Whether the plan takes owner decisions at this time: published or in review, before the cutoff.</summary>
    public static bool IsOpen(PlanRow plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.State is PlanState.Published or PlanState.InReview && now < plan.Plan.ReviewCutoff;
    }
}
