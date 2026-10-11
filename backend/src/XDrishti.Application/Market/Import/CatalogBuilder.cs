using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Import;

/// <summary>
/// Turns the in-scope rows of Dhan's file into the instruments of the master list (ADR 0010): canonical symbols (a collision keeps the first row and is
/// reported, never merged), futures past their expiry dropped, F&amp;O eligibility from active stock futures, sector from the NSE list by ISIN.
/// </summary>
internal static class CatalogBuilder
{
    private const int MaxListedProblems = 10;

    private const string StockFuture = "FUTSTK";

    public static CatalogBuild Build(IReadOnlyList<MasterRow> rows, SectorList? sectors, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var warnings = new List<string>();
        var expired = 0;
        var collisions = 0;
        var bySymbol = new Dictionary<string, MasterRow>(StringComparer.Ordinal);
        var byBrokerId = new Dictionary<(string Segment, string Id), string>();
        var order = new List<(string Symbol, MasterRow Row)>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Type == InstrumentType.Future && row.Expiry < today)
            {
                expired++;
                continue;
            }

            var symbol = SymbolOf(row);
            if (bySymbol.TryGetValue(symbol, out var kept))
            {
                collisions++;
                if (collisions <= MaxListedProblems)
                {
                    warnings.Add($"Symbol {symbol} is used by Dhan ids {kept.SecurityId} and {row.SecurityId}; kept {kept.SecurityId}, skipped {row.SecurityId}.");
                }

                continue;
            }

            if (byBrokerId.TryGetValue((row.Segment, row.SecurityId), out var owner))
            {
                // One broker id cannot name two instruments.
                collisions++;
                if (collisions <= MaxListedProblems)
                {
                    warnings.Add($"Dhan id {row.SecurityId} is used by {owner} and {symbol}; kept {owner}, skipped {symbol}.");
                }

                continue;
            }

            bySymbol.Add(symbol, row);
            byBrokerId.Add((row.Segment, row.SecurityId), symbol);
            order.Add((symbol, row));
        }

        if (collisions > MaxListedProblems)
        {
            warnings.Add($"…and {collisions - MaxListedProblems} more symbol collisions.");
        }

        // F&O eligible: a share with an active stock future.
        var futureUnderlyings = order.Where(o => o.Row.Instrument == StockFuture)
            .Select(o => o.Row.UnderlyingSymbol).ToHashSet(StringComparer.Ordinal);
        var shareTickers = order.Where(o => o.Row.Type == InstrumentType.Share).Select(o => o.Row.UnderlyingSymbol).ToHashSet(StringComparer.Ordinal);
        var orphanFutures = futureUnderlyings.Where(u => !shareTickers.Contains(u)).Order(StringComparer.Ordinal).ToArray();
        if (orphanFutures.Length > 0)
        {
            warnings.Add($"{orphanFutures.Length} stock future underlyings are not in the share list (for example {string.Join(", ", orphanFutures.Take(3))}).");
        }

        var matched = 0;
        var unmatched = 0;
        var candidates = new List<InstrumentCandidate>(order.Count);
        foreach (var (symbol, row) in order)
        {
            var sector = row.Type == InstrumentType.Share ? sectors?.IndustryOf(row.Isin) : null;
            if (row.Type == InstrumentType.Share && sectors is not null)
            {
                if (sector is null)
                {
                    unmatched++;
                }
                else
                {
                    matched++;
                }
            }

            var fno = row.Type == InstrumentType.Share && futureUnderlyings.Contains(row.UnderlyingSymbol);
            candidates.Add(ToCandidate(symbol, row, sector, fno));
        }

        var byType = candidates.GroupBy(c => c.Type.ToCode()).ToDictionary(g => g.Key, g => g.Count());
        var fnoUnderlyings = candidates.Count(c => c.FnoEligible);
        return new CatalogBuild(candidates, expired, collisions, matched, unmatched, fnoUnderlyings, byType, warnings);
    }

    private static string SymbolOf(MasterRow row) => row.Type switch
    {
        InstrumentType.Index => CanonicalSymbol.ForIndex(row.SymbolName),
        InstrumentType.Future => CanonicalSymbol.ForFuture(row.UnderlyingSymbol, row.Expiry!.Value),
        _ => CanonicalSymbol.ForListed(row.UnderlyingSymbol),
    };

    private static InstrumentCandidate ToCandidate(string symbol, MasterRow row, string? sector, bool fno) => new(
        symbol,
        row.Type,
        InstrumentScope.BrokerSegment(row.Segment),
        row.SymbolName,
        row.DisplayName,
        row.Isin,
        row.Series,
        row.LotSize,
        row.TickSize,
        row.Expiry,
        row.UnderlyingSymbol,
        sector,
        sector is null ? null : SectorList.Source,
        fno,
        row.AsmGsmFlag,
        row.AsmGsmCategory,
        row.MtfLeverage,
        row.FreezeQuantity,
        row.SecurityId);
}
