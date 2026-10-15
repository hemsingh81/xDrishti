using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>A scrip name of the report with what it was taken to mean, how many report rows and trades it has and — when not resolved — why not.</summary>
internal sealed record BillNameItem(BillName Name, SecurityMatch Match, int Rows, int Trades);

/// <summary>A day, security and side for which the broker's trades and the report's bill rows have different total quantities (ADR 0029): shown, never hidden.</summary>
internal sealed record CoverageDifference(DateOnly Date, string Isin, string? Symbol, TradeSide Side, long BillQty, long DhanQty);

/// <summary>
/// What importing a global transaction report would do, before the lots are looked at: the trades to consider (the file as a <see cref="TradeFile"/> of the kind the tradebook import plans, with the identity of each), the names and what they were taken to mean, how many
/// trades the broker's own trades cover, where the totals differ, and the report's figures (<see cref="ChargesTotal"/> against the report's own footer). <see cref="Fingerprint"/> fingerprints everything the plan decided that the lot plan does not show.
/// </summary>
internal sealed record BillPlan(
    TradeFile File,
    IReadOnlyList<IdentifiedTrade> Identified,
    IReadOnlyList<BillNameItem> Names,
    int Covered,
    IReadOnlyList<CoverageDifference> Differences,
    int DifferencesTotal,
    int NegativeChargeRows,
    int InexactValues,
    decimal ChargesTotal,
    decimal? FooterCharges,
    string Fingerprint)
{
    /// <summary>Names the application could not pin to one security (no match, or more than one fits).</summary>
    public IEnumerable<BillNameItem> Unresolved => Names.Where(n => !n.Match.IsResolved);

    public int UnresolvedTrades => Unresolved.Sum(n => n.Trades);

    public int Partial => Names.Count(n => n.Match.Kind == SecurityMatchKind.Partial);

    /// <summary>True when the report's charges and its own total differ by more than a paisa.</summary>
    public bool ChargesDiffer => FooterCharges is { } footer && Math.Abs(ChargesTotal - footer) > ChargesTolerance;

    public const decimal ChargesTolerance = 0.01m;
}

/// <summary>
/// Turns a global transaction report into the trades to plan (ADR 0029). Per row, in this order: not equity (futures, options, MCX and other segments) is skipped with its reason; the scrip name is resolved by the <see cref="SecurityIndex"/>; a BSE row whose ISIN the
/// account does not hold is skipped as <c>bse</c> (the same ISIN trades on both exchanges: a held one is the same lot); then each side of the row becomes a trade — unless the broker's own trades for the same day, ISIN and side exist already (they replace the report's
/// bill rows: <see cref="BillTrades.CoveredByDhan"/>, counted, with the days whose totals differ listed). A name that cannot be resolved is kept ISIN-less, like a tradebook line with a symbol only. Pure.
/// </summary>
internal static class BillPlanner
{
    /// <summary>The most days with different totals the plan lists (all are counted).</summary>
    public const int MaxListedDifferences = 50;

    public static BillPlan Plan(GtrFile file, SecurityIndex index, IReadOnlyList<TradeGroup> brokerGroups)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(brokerGroups);
        var dhan = brokerGroups.GroupBy(g => (g.Date, g.Isin, g.Side)).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var names = new Dictionary<string, (BillName Name, SecurityMatch Match, int Rows, int Trades)>(StringComparer.Ordinal);
        var parsed = new List<ParsedTrade>();
        var pending = new List<(ParsedTrade Trade, GtrRow Row, TradeSide Side, BillName Name)>();
        var covered = 0;
        var billQty = new Dictionary<(DateOnly, string, TradeSide), long>();
        var negative = 0;
        var inexact = 0;

        // The plain identities of the NSE legs of the file, whatever becomes of them below (left out as covered by Dhan): a BSE trade whose NSE sibling is left out must still be told apart from it (ADR 0030).
        var nseLegs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in file.Items)
        {
            var name = BillName.Parse(row.Name);
            if (BillTrades.KeepsPlainIdentity(row) && BillTrades.NotEquity(row, name) is null)
            {
                foreach (var leg in BillTrades.Legs(row))
                {
                    nseLegs.Add(BillTrades.Identity(row, leg.Side, name));
                }
            }
        }

        foreach (var row in file.Items)
        {
            var name = BillName.Parse(row.Name);
            if (BillTrades.NotEquity(row, name) is { } reason)
            {
                skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                continue;
            }

            var match = index.Match(name);
            var isin = match.Security?.Isin;
            var bse = BillTrades.IsBse(row);
            if (bse && !index.Holds(isin))
            {
                skipped[TradeSkips.Bse] = skipped.GetValueOrDefault(TradeSkips.Bse) + 1;
                continue;
            }

            var legs = BillTrades.Legs(row);
            negative += row.Charges < 0m ? 1 : 0;
            var nameKey = name.Key + "\u0001" + name.SymbolHint;
            names.TryGetValue(nameKey, out var seen);
            var taken = 0;
            foreach (var leg in legs)
            {
                if (isin is not null && dhan.ContainsKey((row.Date, isin, leg.Side)))
                {
                    covered++;
                    billQty[(row.Date, isin, leg.Side)] = billQty.GetValueOrDefault((row.Date, isin, leg.Side)) + leg.Qty;
                    skipped[BillTrades.CoveredByDhan] = skipped.GetValueOrDefault(BillTrades.CoveredByDhan) + 1;
                    continue;
                }

                inexact += leg.Exact ? 0 : 1;
                var trade = new ParsedTrade(
                    row.Line,
                    Symbol(match, name),
                    isin,
                    bse ? "BSE" : "NSE",
                    leg.Side,
                    row.Date,
                    null,
                    leg.Qty,
                    leg.Price,
                    leg.Charges,
                    null,
                    null);
                parsed.Add(trade);
                pending.Add((trade, row, leg.Side, name));
                taken++;
            }

            names[nameKey] = (name, match, seen.Rows + 1, seen.Trades + taken);
        }

        var identified = Identify(pending, nseLegs);
        var differences = billQty
            .Where(q => dhan[(q.Key.Item1, q.Key.Item2, q.Key.Item3)] != q.Value)
            .OrderBy(q => q.Key.Item1).ThenBy(q => q.Key.Item2, StringComparer.Ordinal).ThenBy(q => q.Key.Item3)
            .Select(q => new CoverageDifference(q.Key.Item1, q.Key.Item2, SymbolOf(index, q.Key.Item2), q.Key.Item3, q.Value, dhan[(q.Key.Item1, q.Key.Item2, q.Key.Item3)]))
            .ToList();
        var items = names.Values.Select(n => new BillNameItem(n.Name, n.Match, n.Rows, n.Trades)).OrderBy(n => n.Name.Display, StringComparer.Ordinal).ThenBy(n => n.Name.SymbolHint, StringComparer.Ordinal).ToList();
        var tradeFile = new TradeFile(file.Rows, parsed, file.Repeated, skipped.Values.Sum(), skipped, file.Bad, file.BadTotal);
        return new BillPlan(
            tradeFile,
            identified,
            items,
            covered,
            [.. differences.Take(MaxListedDifferences)],
            differences.Count,
            negative,
            inexact,
            file.ChargesTotal,
            file.FooterCharges,
            Fingerprint(covered, differences.Count, items, skipped));
    }

    /// <summary>
    /// The identity of each trade. A trade keeps the plain identity (which stored trades have) unless another pending trade shares it - the same bill number, scrip, day and side on another exchange. Then the NSE trade keeps the plain identity and every other one adds its
    /// exchange (a blank exchange adds a fixed token), so the identities do not depend on the order of the rows: the same pair exported the other way round is the same trades, not new ones.
    /// <b>One more case</b> (ADR 0030): a trade of another named exchange (the BSE) whose NSE sibling was left out - covered by the broker's own trades - also adds its exchange, so it has the identity it has when the NSE sibling is pending. Nothing else depends on trades that
    /// are not pending: a blank exchange, two BSE legs of which one is skipped, or an NSE trade whose BSE sibling is skipped keep the identity they had before. <paramref name="nseLegs"/> holds the plain identities of the NSE legs of the whole file.
    /// </summary>
    private static List<IdentifiedTrade> Identify(List<(ParsedTrade Trade, GtrRow Row, TradeSide Side, BillName Name)> pending, HashSet<string> nseLegs)
    {
        var plain = pending.Select(p => BillTrades.Identity(p.Row, p.Side, p.Name)).ToList();
        var shared = plain.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var plainTaken = new HashSet<string>(StringComparer.Ordinal);
        var identified = new List<IdentifiedTrade>(pending.Count);
        for (var i = 0; i < pending.Count; i++)
        {
            var (trade, row, side, name) = pending[i];
            var id = plain[i];
            var salted = shared.Contains(id)
                ? !BillTrades.KeepsPlainIdentity(row) || !plainTaken.Add(id)
                : BillTrades.IsOtherExchange(row) && nseLegs.Contains(id);
            if (salted)
            {
                id = BillTrades.Identity(row, side, name, BillTrades.CollisionSalt(row));
            }

            identified.Add(new IdentifiedTrade(trade, id));
        }

        return identified;
    }

    private static string Symbol(SecurityMatch match, BillName name)
    {
        var symbol = match.Security?.Symbol ?? name.SymbolHint ?? TradeRules.NormaliseSymbol(name.Base);
        return symbol.Length <= Holding.MaxSymbolLength ? symbol : symbol[..Holding.MaxSymbolLength];
    }

    private static string? SymbolOf(SecurityIndex index, string isin) => index.ByIsin(isin).Symbol;

    private static string Fingerprint(int covered, int differences, IReadOnlyList<BillNameItem> names, IReadOnlyDictionary<string, int> skipped)
    {
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"v1|{covered}|{differences}|{string.Join(',', skipped.Select(s => $"{s.Key}={s.Value}"))}"));
        foreach (var item in names)
        {
            text.Append('|').Append(item.Name.Key).Append('=').Append(item.Match.Security?.Identity ?? item.Match.Kind.ToString());
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
