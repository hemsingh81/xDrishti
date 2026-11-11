using System.Globalization;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Strength;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Strength;

namespace XDrishti.Application.Market.Strength;

/// <summary>The stored relative-strength rows (BK-09), newest first, at most <see cref="GetRelativeStrengthHandler.MaxRows"/>: of one pre-registered basket (an id or its name) and one session (<c>yyyy-MM-dd</c>) when asked. A read: it writes nothing.</summary>
public sealed record GetRelativeStrengthQuery(string? Basket = null, string? AsOf = null) : IQuery<Result<RelativeStrengthView>>;

/// <summary>One stored row. <c>basketId</c> is the empty id for the index-relative row (lookback 20; <c>score</c> is percentage points).</summary>
public sealed record RelativeStrengthRowView(DateOnly AsOf, Guid InstrumentId, Guid BasketId, int Lookback, int Skip, double? Score, double? PctRank, int NRanked, string State, string MembershipBasis);

/// <summary>The rows and the cap they were cut at.</summary>
public sealed record RelativeStrengthView(IReadOnlyList<RelativeStrengthRowView> Items, int Cap);

internal sealed class GetRelativeStrengthHandler(IRelativeStrengthStore store, IBasketQueries baskets) : IQueryHandler<GetRelativeStrengthQuery, Result<RelativeStrengthView>>
{
    public const int MaxRows = 500;

    public const string Invalid = "market.relative_strength.invalid";

    public async ValueTask<Result<RelativeStrengthView>> HandleAsync(GetRelativeStrengthQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateOnly? asOf = null;
        if (!string.IsNullOrWhiteSpace(query.AsOf))
        {
            if (!DateOnly.TryParseExact(query.AsOf.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return Result.Failure<RelativeStrengthView>(Error.Validation(Invalid, "asOf is a date such as 2026-10-30."));
            }

            asOf = date;
        }

        Guid? basketId = null;
        if (!string.IsNullOrWhiteSpace(query.Basket))
        {
            var text = query.Basket.Trim();
            if (Guid.TryParse(text, out var id))
            {
                basketId = id;
            }
            else if (StrengthBaskets.TryCode(text, out var code))
            {
                var found = StrengthBasketNames.Find(await baskets.ListAsync(false, cancellationToken), StrengthBaskets.NameOf(code)!);
                if (found is null)
                {
                    return Result.Success(new RelativeStrengthView([], MaxRows));
                }

                basketId = found.Id;
            }
            else
            {
                return Result.Failure<RelativeStrengthView>(Error.Validation(Invalid, "basket is a basket id or one of " + string.Join(", ", StrengthBaskets.Names.Select(n => "\"" + n + "\"")) + "."));
            }
        }

        var rows = await store.ListAsync(basketId, asOf, MaxRows, cancellationToken);
        return Result.Success(new RelativeStrengthView(
            [.. rows.Select(r => new RelativeStrengthRowView(r.AsOf, r.InstrumentId, r.BasketId, r.Lookback, r.Skip, r.Score, r.PctRank, r.NRanked, r.State, r.MembershipBasis))], MaxRows));
    }
}
