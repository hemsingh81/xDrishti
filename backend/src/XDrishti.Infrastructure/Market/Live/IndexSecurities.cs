using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Live;

/// <summary>
/// The indices of the master list that the feed streams (ADR 0024 §2): instruments of type <c>index</c>, not retired, that have a Dhan broker id in the index segment <c>IDX_I</c>. An index is found by its canonical symbol first
/// (<c>NSE:NIFTY</c>), then by its display name (<c>Nifty 50</c>) ignoring case; the list of indices is small, so the match is made in memory after one query.
/// </summary>
internal sealed class IndexSecurities(XDrishtiDbContext db) : IIndexSecurities
{
    private const string DhanBroker = "dhan";

    public async ValueTask<IReadOnlyList<IndexSecurity>> FindAsync(IReadOnlyList<IndexName> names, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);
        var indices = await ListAsync(null, cancellationToken);
        var found = new List<IndexSecurity>();
        foreach (var name in names)
        {
            var match = indices.FirstOrDefault(i => string.Equals(i.CanonicalSymbol, name.CanonicalSymbol, StringComparison.OrdinalIgnoreCase))
                ?? indices.FirstOrDefault(i => string.Equals(i.DisplayName, name.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (match is not null && found.All(f => f.InstrumentId != match.Id))
            {
                found.Add(new IndexSecurity(name.Label, match.Id, match.BrokerId));
            }
        }

        return found;
    }

    public async ValueTask<IndexSecurity?> FindByIdAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        (await ListAsync(instrumentId, cancellationToken)).Select(i => new IndexSecurity(i.CanonicalSymbol, i.Id, i.BrokerId)).FirstOrDefault();

    private async Task<List<Row>> ListAsync(Guid? instrumentId, CancellationToken cancellationToken) =>
        await (
            from instrument in db.Instruments.AsNoTracking()
            join broker in db.InstrumentBrokerIds.AsNoTracking() on instrument.Id equals broker.InstrumentId
            where instrument.Type == "index" && instrument.RetiredAt == null && broker.Broker == DhanBroker && broker.Segment == FeedSegments.Index
                && (instrumentId == null || instrument.Id == instrumentId)
            orderby instrument.CanonicalSymbol
            select new Row(instrument.Id, instrument.CanonicalSymbol, instrument.DisplayName, broker.BrokerId))
            .ToListAsync(cancellationToken);

    private sealed record Row(Guid Id, string CanonicalSymbol, string DisplayName, string BrokerId);
}
