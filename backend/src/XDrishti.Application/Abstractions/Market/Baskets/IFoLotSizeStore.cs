namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>One dated lot size to store.</summary>
public sealed record LotWrite(Guid InstrumentId, int LotSize, string ExpiryLabel);

/// <summary>The dated F&amp;O lot sizes of NSE's file (<c>market.fo_lot_sizes</c>, insert-only). The master's own lot size is never changed by it.</summary>
public interface IFoLotSizeStore
{
    /// <summary>The newest stored lot size of each instrument.</summary>
    ValueTask<IReadOnlyDictionary<Guid, int>> LatestAsync(CancellationToken cancellationToken);

    /// <summary>Inserts the rows dated <paramref name="effectiveFrom"/> (a row that exists for that date is left alone) together with the fetch row and the source's new state, in one transaction.</summary>
    ValueTask ApplyAsync(SourceUpdate source, FetchRecord fetch, DateOnly effectiveFrom, IReadOnlyList<LotWrite> rows, CancellationToken cancellationToken);
}
