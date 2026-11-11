using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;

namespace XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

/// <summary>The basket profile as the tool server gets it (BK-12b): the same handler interface as the API's own, answered by the API's read-only route <c>GET /api/reports/baskets/{id}/profile</c>, so that <c>BasketReportReader</c> and its shaping are unchanged.</summary>
internal sealed class ApiBasketProfileHandler(BasketApiClient client) : IQueryHandler<GetBasketProfileQuery, Result<BasketProfileView>>
{
    public const string Route = "basket_profile";

    public ValueTask<Result<BasketProfileView>> HandleAsync(GetBasketProfileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Guid.TryParse(query.Id, out var id))
        {
            return ValueTask.FromResult(Result.Failure<BasketProfileView>(Error.Validation(ToolErrorCodes.InvalidArguments, "basket: give a basket id.")));
        }

        var path = $"/api/reports/baskets/{id:D}/profile" + BasketApiQuery.Of(("from", query.From), ("to", query.To), ("index", query.Index), ("membersAsOf", query.MembersAsOf));
        return client.GetAsync<BasketProfileView>(Route, path, cancellationToken);
    }
}
