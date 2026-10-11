using XDrishti.Application.Market.Search;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>Read side of the master list (projected, not tracked).</summary>
public interface IInstrumentQueries
{
    ValueTask<InstrumentPage> SearchAsync(InstrumentSearch search, CancellationToken cancellationToken);

    ValueTask<InstrumentFacets> FacetsAsync(CancellationToken cancellationToken);

    ValueTask<InstrumentDetail?> GetAsync(Guid id, CancellationToken cancellationToken);
}
