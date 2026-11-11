using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Context;

/// <summary>One stored day of a basket's context. A figure that is not a number is null; <c>state</c> is value or insufficient; <c>membershipBasis</c> is point_in_time or single_snapshot (never presented as point in time).</summary>
public sealed record BasketContextDay(DateOnly AsOf, int NMembers, int NVoters, double? B20, double? B50, double? B200, double? AdShare, double? NewHighShare, double? Dispersion, double? RhoBar, string State, string MembershipBasis);

/// <summary>The newest stored rows of one basket (at most 400), newest first. Descriptive only: nothing here is a signal.</summary>
public sealed record BasketContextView(Guid BasketId, IReadOnlyList<BasketContextDay> Items);

/// <summary>Reads the stored context of a basket; both dates optional. A missing basket id or an inverted range is <c>baskets.invalid</c>.</summary>
public sealed record GetBasketContextQuery(Guid? Basket, DateOnly? From, DateOnly? To) : IQuery<Result<BasketContextView>>;

internal sealed class GetBasketContextHandler(IBasketContextStore store) : IQueryHandler<GetBasketContextQuery, Result<BasketContextView>>
{
    public const int MaxRows = 400;

    public async ValueTask<Result<BasketContextView>> HandleAsync(GetBasketContextQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Basket is not { } id || id == Guid.Empty)
        {
            return Result.Failure<BasketContextView>(Error.Validation("baskets.invalid", "basket: give the basket id."));
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            return Result.Failure<BasketContextView>(Error.Validation("baskets.invalid", "from must not be after to."));
        }

        var rows = await store.ListAsync(id, query.From, query.To, MaxRows, cancellationToken);
        return Result.Success(new BasketContextView(id, [.. rows.Select(r => new BasketContextDay(r.AsOf, r.NMembers, r.NVoters, r.B20, r.B50, r.B200, r.AdShare, r.NewHighShare, r.Dispersion, r.RhoBar, r.State, r.MembershipBasis))]));
    }
}
