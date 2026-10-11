using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Search;

/// <summary>One instrument with everything the master list knows about it.</summary>
public sealed record GetInstrumentQuery(Guid Id) : IQuery<Result<InstrumentDetail>>;

internal sealed class GetInstrumentHandler(IInstrumentQueries queries) : IQueryHandler<GetInstrumentQuery, Result<InstrumentDetail>>
{
    public async ValueTask<Result<InstrumentDetail>> HandleAsync(GetInstrumentQuery query, CancellationToken cancellationToken) =>
        await queries.GetAsync(query.Id, cancellationToken) is { } detail
            ? Result.Success(detail)
            : Result.Failure<InstrumentDetail>(MarketErrors.NotFound);
}
