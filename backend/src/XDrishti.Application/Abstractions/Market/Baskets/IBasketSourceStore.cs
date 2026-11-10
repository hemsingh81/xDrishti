using XDrishti.Application.Market.Baskets;

namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>Persistence of the catalogue, the instrument matching and the fetch log. Reads are not tracked; <see cref="RecordAsync"/> writes and saves.</summary>
public interface IBasketSourceStore
{
    /// <summary>The catalogue rows (all, or only those of the given baskets), in name order.</summary>
    ValueTask<IReadOnlyList<BasketSourceInfo>> ListAsync(IReadOnlyCollection<Guid>? basketIds, CancellationToken cancellationToken);

    /// <summary>Instruments of the master (shares, retired ones too) whose ISIN is among <paramref name="isins"/> or whose symbol is among <paramref name="symbols"/>.</summary>
    ValueTask<IReadOnlyList<MasterInstrument>> FindInstrumentsAsync(IReadOnlyCollection<string> isins, IReadOnlyCollection<string> symbols, CancellationToken cancellationToken);

    /// <summary>The newest fetches of a basket, newest first.</summary>
    ValueTask<IReadOnlyList<FetchOutcomeView>> RecentFetchesAsync(Guid basketId, int take, CancellationToken cancellationToken);

    /// <summary>Turns a source on or off and saves it together with the audit events collected meanwhile; false when there is no such source or it is in that state already.</summary>
    ValueTask<bool> SetEnabledAsync(Guid basketId, bool enabled, CancellationToken cancellationToken);

    /// <summary>Stores the fetch row and the source's new state in one transaction (nothing about the members changes).</summary>
    ValueTask RecordAsync(SourceUpdate source, FetchRecord fetch, CancellationToken cancellationToken);
}
