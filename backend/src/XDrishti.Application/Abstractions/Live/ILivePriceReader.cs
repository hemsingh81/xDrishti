using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Abstractions.Live;

/// <summary>Reads the latest price of instruments for the hub: the newest price time among the open holdings' price columns (<c>portfolio.holdings</c>: sync, quote or live) and <c>market.live_quotes</c>. Instruments without a price are left out.</summary>
public interface ILivePriceReader
{
    ValueTask<IReadOnlyList<InstrumentPrice>> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);
}
