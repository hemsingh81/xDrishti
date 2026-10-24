using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>The plan and the ticket an owner decision is about, and the guards every decision shares (spec P7-10): an owner actor, the ticket is in that plan.</summary>
internal static class ReviewLookup
{
    public static Error? OwnerGuard(string? actor) =>
        string.IsNullOrWhiteSpace(actor) || actor.Length > 100 || !PlanActors.IsOwner(actor) || string.IsNullOrWhiteSpace(actor[PlanActors.OwnerPrefix.Length..]) ? PlanErrors.OwnerOnly() : null;

    /// <summary>A reason of 1 to 500 characters, or the default when none was given.</summary>
    public static Result<string> Reason(string? given, string fallback)
    {
        if (given is null)
        {
            return Result.Success(fallback);
        }

        var text = given.Trim();
        return text.Length is >= 1 and <= 500 ? Result.Success(text) : Result.Failure<string>(PlanErrors.Invalid([new FieldError("reason", "The reason is 1 to 500 characters.")]));
    }

    public static async ValueTask<Result<(PlanRow Plan, PlanItemRow Item)>> FindAsync(IPlanStore store, long planId, long itemId, CancellationToken cancellationToken)
    {
        var plan = await store.FindAsync(planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<(PlanRow, PlanItemRow)>(PlanErrors.NotFound(planId));
        }

        var item = await store.FindItemAsync(itemId, cancellationToken);
        return item is null || item.PlanId != planId ? Result.Failure<(PlanRow, PlanItemRow)>(PlanErrors.ItemNotFound(itemId)) : Result.Success((plan, item));
    }
}
