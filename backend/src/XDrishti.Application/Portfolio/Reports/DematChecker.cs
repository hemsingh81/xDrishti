using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>What a demat holding statement says against the account's synced holdings (ADR 0029): the securities that agree, and those that do not.</summary>
internal sealed record DematComparison(
    int Securities,
    int Matching,
    IReadOnlyList<DematDifference> Differences,
    IReadOnlyList<DematHeld> HeldNotSynced,
    IReadOnlyList<DematSynced> SyncedNotInStatement);

/// <summary>
/// Compares a demat holding statement with the account's open holdings (ADR 0029); nothing is changed by it. Securities are joined by ISIN (a holding without an ISIN by the key of its name); a security listed twice, or a holding listed per exchange, counts as the sum of its
/// quantities. <c>Matching</c> securities have the same quantity; <c>Differences</c> have a holding with another quantity; <c>HeldNotSynced</c> are securities of the statement with a quantity above 0 and no open holding; <c>SyncedNotInStatement</c> are open holdings the statement
/// does not list. Pure.
/// </summary>
internal static class DematChecker
{
    public static DematComparison Compare(DematFile file, IReadOnlyList<HoldingData> holdings)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(holdings);
        var statement = file.Items
            .GroupBy(r => r.Isin, StringComparer.Ordinal)
            .Select(g => (Isin: g.Key, Name: g.First().Name, Quantity: g.Sum(r => r.Quantity)))
            .OrderBy(s => s.Isin, StringComparer.Ordinal)
            .ToList();
        var open = holdings.Where(h => h.IsOpen).ToList();
        var byIsin = open.Where(h => TradeRules.NormaliseIsin(h.Isin) is not null).GroupBy(h => TradeRules.NormaliseIsin(h.Isin)!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var byName = open.Where(h => TradeRules.NormaliseIsin(h.Isin) is null).GroupBy(h => DividendNames.Key(h.Name), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var used = new HashSet<Guid>();
        var matching = 0;
        var differences = new List<DematDifference>();
        var held = new List<DematHeld>();
        foreach (var (isin, name, quantity) in statement)
        {
            if (!byIsin.TryGetValue(isin, out var group) && !byName.TryGetValue(DividendNames.Key(name), out group))
            {
                if (quantity > 0m)
                {
                    held.Add(new DematHeld(isin, name, quantity));
                }

                continue;
            }

            foreach (var holding in group)
            {
                used.Add(holding.Id);
            }

            var synced = group.Sum(h => (decimal)h.Qty);
            if (synced == quantity)
            {
                matching++;
            }
            else
            {
                differences.Add(new DematDifference(isin, name, quantity, synced));
            }
        }

        var missing = open
            .Where(h => !used.Contains(h.Id) && h.Qty > 0)
            .OrderBy(h => h.TradingSymbol, StringComparer.Ordinal)
            .ThenBy(h => h.Id)
            .Select(h => new DematSynced(h.TradingSymbol, TradeRules.NormaliseIsin(h.Isin), h.Name, h.Qty))
            .ToList();
        return new DematComparison(statement.Count, matching, differences, held, missing);
    }
}
