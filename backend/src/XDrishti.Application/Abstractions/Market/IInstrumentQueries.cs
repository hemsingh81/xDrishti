using XDrishti.Application.Market.Search;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>Read side of the master list (projected, not tracked).</summary>
public interface IInstrumentQueries
{
    ValueTask<InstrumentPage> SearchAsync(InstrumentSearch search, CancellationToken cancellationToken);

    ValueTask<InstrumentFacets> FacetsAsync(CancellationToken cancellationToken);

    ValueTask<InstrumentDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The master-list rows that these upper-case keys name: <paramref name="symbols"/> are canonical symbols (compared ignoring case), <paramref name="isins"/>
    /// are ISINs of shares and ETFs. Retired rows are included (flagged). No other matching is done here.
    /// </summary>
    ValueTask<IReadOnlyList<ResolveCandidate>> FindForResolveAsync(
        IReadOnlyCollection<string> symbols, IReadOnlyCollection<string> isins, CancellationToken cancellationToken);
}
