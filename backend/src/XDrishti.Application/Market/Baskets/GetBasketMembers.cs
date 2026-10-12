using System.Globalization;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// The members of a basket (archived ones too), in their order. <c>AsOf</c> is an India date <c>yyyy-MM-dd</c>: the members at the end of that day, with
/// the removed ones showing their end date; without it the members now. A system (portfolio) basket gives the instruments of the bucket's holdings now with
/// quantity and account; <c>AsOf</c> on it is <c>baskets.invalid</c>.
/// </summary>
public sealed record GetBasketMembersQuery(Guid BasketId, string? AsOf = null) : IQuery<Result<BasketMembers>>;

internal sealed class GetBasketMembersHandler(IBasketQueries queries, IPortfolioBasketSource portfolio) : IQueryHandler<GetBasketMembersQuery, Result<BasketMembers>>
{
    public async ValueTask<Result<BasketMembers>> HandleAsync(GetBasketMembersQuery query, CancellationToken cancellationToken)
    {
        DateOnly? asOf = null;
        if (!string.IsNullOrWhiteSpace(query.AsOf))
        {
            if (!DateOnly.TryParseExact(query.AsOf.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < 2000 or > 2999)
            {
                return Result.Failure<BasketMembers>(BasketErrors.Invalid("asOf", "Use a date such as 2026-10-11."));
            }

            asOf = date;
        }

        var members = await queries.MembersAsync(query.BasketId, asOf is { } day ? BasketMember.CutoffAfter(day) : null, asOf, cancellationToken);
        if (members is not null)
        {
            return Result.Success(members);
        }

        if (!await portfolio.ExistsAsync(query.BasketId, cancellationToken))
        {
            return Result.Failure<BasketMembers>(BasketErrors.NotFound);
        }

        if (asOf is not null)
        {
            return Result.Failure<BasketMembers>(BasketErrors.Invalid("asOf", "A portfolio basket has no past members: it shows the current holdings."));
        }

        var holdings = await portfolio.MembersAsync(query.BasketId, cancellationToken);
        return holdings is null ? Result.Failure<BasketMembers>(BasketErrors.NotFound) : Result.Success(holdings);
    }
}
