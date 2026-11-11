using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets.Lists;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>The stored master for the list baskets: the shares with their flags (two queries over the instrument table, no network) and the lot size of each underlying's nearest unexpired future.</summary>
internal sealed class ListMasterReader(XDrishtiDbContext db) : IListMasterReader
{
    public async ValueTask<IReadOnlyDictionary<string, Guid>> ReadIndexIdsAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbols);
        var rows = await db.Instruments.AsNoTracking()
            .Where(i => i.Type == InstrumentTypeCodes.Index && i.RetiredAt == null && canonicalSymbols.Contains(i.CanonicalSymbol))
            .Select(i => new { i.Id, i.CanonicalSymbol })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.CanonicalSymbol, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlyList<MasterShare>> ReadSharesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var shares = await db.Instruments.AsNoTracking()
            .Where(i => i.Type == InstrumentTypeCodes.Share)
            .Select(i => new { i.Id, i.CanonicalSymbol, i.UnderlyingSymbol, i.Series, i.FnoEligible, i.AsmGsmFlag, i.AsmGsmCategory, i.MtfLeverage, i.RetiredAt })
            .ToListAsync(cancellationToken);
        var futures = await db.Instruments.AsNoTracking()
            .Where(i => i.Type == InstrumentTypeCodes.Future && i.RetiredAt == null && i.ExpiryDate >= today)
            .Select(i => new { i.UnderlyingSymbol, i.ExpiryDate, i.LotSize })
            .ToListAsync(cancellationToken);
        var nearest = futures.GroupBy(f => f.UnderlyingSymbol, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(f => f.ExpiryDate).First().LotSize, StringComparer.Ordinal);
        return [.. shares.Select(s => new MasterShare(
            new FlagRow(s.Id, s.Series, s.FnoEligible, s.AsmGsmFlag, s.AsmGsmCategory, s.MtfLeverage, s.RetiredAt is not null),
            s.CanonicalSymbol.StartsWith("NSE:", StringComparison.Ordinal) ? s.CanonicalSymbol[4..] : s.CanonicalSymbol,
            nearest.TryGetValue(s.UnderlyingSymbol, out var lot) && lot > 0 ? lot : null))];
    }
}
