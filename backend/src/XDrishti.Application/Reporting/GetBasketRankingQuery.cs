using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>Baskets side by side on relative strength (BK-07). All fields are the raw text of the request and are checked by the handler.</summary>
/// <param name="Basket">Basket ids, comma separated (2 to 12).</param>
/// <param name="From">First India date, yyyy-MM-dd.</param>
/// <param name="To">Last India date, yyyy-MM-dd.</param>
/// <param name="Index">The index instrument id; default the owner's benchmark.</param>
public sealed record GetBasketRankingQuery(string? Basket, string? From, string? To, string? Index) : IQuery<Result<BasketRankingView>>;
