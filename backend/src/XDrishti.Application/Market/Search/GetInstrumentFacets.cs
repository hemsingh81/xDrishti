using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Market.Search;

/// <summary>Counts of the active instruments by type, sector, series, F&amp;O eligibility and ASM/GSM flag, to build the filters.</summary>
public sealed record GetInstrumentFacetsQuery : IQuery<InstrumentFacets>;

internal sealed class GetInstrumentFacetsHandler(IInstrumentQueries queries) : IQueryHandler<GetInstrumentFacetsQuery, InstrumentFacets>
{
    public ValueTask<InstrumentFacets> HandleAsync(GetInstrumentFacetsQuery query, CancellationToken cancellationToken) =>
        queries.FacetsAsync(cancellationToken);
}
