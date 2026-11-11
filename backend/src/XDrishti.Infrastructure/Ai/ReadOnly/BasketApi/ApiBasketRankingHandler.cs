using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;

namespace XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

/// <summary>The basket ranking as the tool server gets it (BK-12b): the API's own handler interface, answered by <c>GET /api/reports/baskets/ranking</c> (see <see cref="ApiBasketProfileHandler"/>).</summary>
internal sealed class ApiBasketRankingHandler(BasketApiClient client) : IQueryHandler<GetBasketRankingQuery, Result<BasketRankingView>>
{
    public const string Route = "basket_ranking";

    public ValueTask<Result<BasketRankingView>> HandleAsync(GetBasketRankingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var ids = (query.Basket ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0 || !ids.All(i => Guid.TryParse(i, out _)))
        {
            return ValueTask.FromResult(Result.Failure<BasketRankingView>(Error.Validation(ToolErrorCodes.InvalidArguments, "baskets: give basket ids, separated by commas.")));
        }

        var path = "/api/reports/baskets/ranking" + BasketApiQuery.Of(("basket", string.Join(',', ids.Select(i => Guid.Parse(i).ToString("D")))), ("from", query.From), ("to", query.To), ("index", query.Index));
        return client.GetAsync<BasketRankingView>(Route, path, cancellationToken);
    }
}
