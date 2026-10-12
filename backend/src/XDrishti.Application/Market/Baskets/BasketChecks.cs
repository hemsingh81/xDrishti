using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>Checks shared by the basket use cases.</summary>
internal static class BasketChecks
{
    /// <summary>Times a change is tried when the database refuses it because of a concurrent change (it reads the basket again each time).</summary>
    public const int Attempts = 5;

    /// <summary>The instrument ids of an add, remove or transfer call: 1 to 500, none empty; repeats count once.</summary>
    public static Result<Guid[]> InstrumentIds(IReadOnlyList<Guid>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return Result.Failure<Guid[]>(BasketErrors.Invalid("instrumentIds", "Pick at least one instrument."));
        }

        if (ids.Count > BasketLimits.MaxIdsPerCall)
        {
            return Result.Failure<Guid[]>(BasketErrors.Invalid("instrumentIds", $"Use at most {BasketLimits.MaxIdsPerCall} instruments per call."));
        }

        return ids.Contains(Guid.Empty)
            ? Result.Failure<Guid[]>(BasketErrors.Invalid("instrumentIds", "An instrument id is empty."))
            : Result.Success(ids.Distinct().ToArray());
    }

    /// <summary>
    /// The ids of a reorder call: each of them once and none empty (that they are exactly the current ones is checked against the stored state).
    /// </summary>
    public static Result<Guid[]> OrderedIds(IReadOnlyList<Guid>? ids, string field, int max)
    {
        if (ids is null)
        {
            return Result.Failure<Guid[]>(BasketErrors.Invalid(field, "Send the complete list in the wanted order."));
        }

        if (ids.Count > max)
        {
            return Result.Failure<Guid[]>(BasketErrors.Invalid(field, $"Use at most {max:N0} entries."));
        }

        if (ids.Contains(Guid.Empty))
        {
            return Result.Failure<Guid[]>(BasketErrors.Invalid(field, "An id is empty."));
        }

        return ids.Distinct().Count() != ids.Count
            ? Result.Failure<Guid[]>(BasketErrors.Invalid(field, "An id appears more than once."))
            : Result.Success(ids.ToArray());
    }

    /// <summary>The basket, when it exists and may be changed (not archived).</summary>
    public static async ValueTask<Result<Basket>> EditableAsync(IBasketStore store, Guid id, CancellationToken cancellationToken)
    {
        var basket = await store.FindAsync(id, cancellationToken);
        if (basket is null)
        {
            return Result.Failure<Basket>(BasketErrors.NotFound);
        }

        return basket.IsArchived ? Result.Failure<Basket>(BasketErrors.Archived) : Result.Success(basket);
    }

    /// <summary>Whether the baskets in this list have the name, ignoring case.</summary>
    public static bool NameTaken(IEnumerable<Basket> active, string name, Guid? except = null) =>
        active.Any(b => b.Id != except && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The wording of the audit trail and of tracking for a basket: <c>basket 'Name'</c>.</summary>
    public static string Called(Basket basket) => $"basket '{basket.Name}'";
}
