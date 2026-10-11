using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

/// <summary>Maps broker holdings to the master list: by ISIN first (shares and ETFs, preferring series EQ), then by Dhan's security id within the NSE equity segment.</summary>
internal sealed class InstrumentMapper(XDrishtiDbContext db) : IInstrumentMapper
{
    private const string DhanBroker = "dhan";

    private const string NseEquitySegment = "NSE_EQ";

    public async ValueTask<IReadOnlyDictionary<string, InstrumentRef>> MapAsync(IReadOnlyCollection<BrokerHolding> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = new Dictionary<string, InstrumentRef>(StringComparer.Ordinal);
        var isins = rows.Where(r => !string.IsNullOrWhiteSpace(r.Isin)).Select(r => r.Isin!.Trim().ToUpperInvariant()).Distinct().ToArray();
        if (isins.Length > 0)
        {
            var byIsin = (await db.Instruments.AsNoTracking()
                .Where(i => i.RetiredAt == null && (i.Type == InstrumentTypeCodes.Share || i.Type == InstrumentTypeCodes.Etf) && i.Isin != null && isins.Contains(i.Isin))
                .Select(i => new { i.Id, i.CanonicalSymbol, i.DisplayName, i.Isin, i.Series })
                .ToListAsync(cancellationToken))
                .GroupBy(i => i.Isin!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(i => i.Series == "EQ" ? 0 : 1).ThenBy(i => i.CanonicalSymbol, StringComparer.Ordinal).First(),
                    StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row.Isin is not null && byIsin.TryGetValue(row.Isin.Trim(), out var found))
                {
                    result[row.Key] = new InstrumentRef(found.Id, found.CanonicalSymbol, found.DisplayName);
                }
            }
        }

        var rest = rows.Where(r => !result.ContainsKey(r.Key) && IsNse(r.Exchange)).ToList();
        if (rest.Count > 0)
        {
            var ids = rest.Select(r => r.SecurityId.Trim()).Distinct().ToArray();
            var bySecurity = (await (
                from b in db.InstrumentBrokerIds.AsNoTracking()
                join i in db.Instruments.AsNoTracking() on b.InstrumentId equals i.Id
                where b.Broker == DhanBroker && b.Segment == NseEquitySegment && ids.Contains(b.BrokerId) && i.RetiredAt == null
                select new { b.BrokerId, i.Id, i.CanonicalSymbol, i.DisplayName })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.BrokerId, StringComparer.Ordinal);
            foreach (var row in rest)
            {
                if (bySecurity.TryGetValue(row.SecurityId.Trim(), out var found))
                {
                    result[row.Key] = new InstrumentRef(found.Id, found.CanonicalSymbol, found.DisplayName);
                }
            }
        }

        return result;
    }

    /// <summary>Dhan reports NSE holdings as "NSE", or "ALL" for a holding it merges across exchanges.</summary>
    private static bool IsNse(string exchange) => exchange.Trim().ToUpperInvariant() is "NSE" or "ALL";
}
