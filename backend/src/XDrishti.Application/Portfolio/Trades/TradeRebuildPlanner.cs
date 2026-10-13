using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>A trade of the file as a transaction (not stored yet unless <see cref="AlreadyStored"/>).</summary>
internal sealed record FileTrade(Transaction Trade, bool AlreadyStored);

/// <summary>One security of a tradebook: how many of its trades the file has, and what rebuilding its holding would do.</summary>
internal sealed record SecurityPlan(
    string Key, string Symbol, string? Isin, bool Matched, Guid? HoldingId, int Trades, int Buys, int Sells, DateOnly FirstTradeDate, DateOnly LastTradeDate, HoldingRebuild Rebuild);

/// <summary>
/// What importing a file would do: the securities of the file with their rebuilds, how many have no holding and how many of those are not even NSE shares or ETFs of the master list, and a digest of all of it — what the preview shows the owner
/// and what an import with rebuild must echo.
/// </summary>
internal sealed record TradePlan(IReadOnlyList<SecurityPlan> Securities, int NotHeldSecurities, int NotHeldTrades, int UnmatchedSecurities, int UnmatchedTrades, string Digest);

/// <summary>
/// Plans the import of a tradebook (ADR 0023): groups the file's trades by security, matches each security to a holding of the account (<see cref="HoldingMatcher"/>) or to the master list, and for each matched holding rebuilds the lots from the
/// trades already stored plus the new ones of the file (<see cref="HoldingRebuilds"/>). Nothing is stored or changed. Pure.
/// </summary>
internal static class TradeRebuildPlanner
{
    public static TradePlan Plan(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<Transaction> stored,
        IReadOnlyList<FileTrade> file,
        TradeInstruments master,
        IReadOnlyDictionary<Guid, IReadOnlyList<AppliedAction>> actions,
        DateOnly firstImportDate)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(actions);
        var matcher = new HoldingMatcher(holdings);
        var storedByHolding = HoldingRebuilds.Group(matcher, stored);
        var newByHolding = HoldingRebuilds.Group(matcher, file.Where(f => !f.AlreadyStored).Select(f => f.Trade));
        var groups = new Dictionary<string, List<FileTrade>>(StringComparer.Ordinal);
        var matches = new Dictionary<string, HoldingMatch>(StringComparer.Ordinal);
        foreach (var item in file)
        {
            var match = matcher.Find(item.Trade.Isin, item.Trade.TradingSymbol);
            var identity = item.Trade.Isin ?? item.Trade.TradingSymbol;
            var key = match.Kind switch
            {
                MatchKind.One => "h:" + match.Holding!.Id.ToString("N"),
                MatchKind.Ambiguous => "a:" + identity,
                _ => "u:" + identity,
            };
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
                matches[key] = match;
            }

            list.Add(item);
        }

        var securities = new List<SecurityPlan>(groups.Count);
        foreach (var (key, items) in groups)
        {
            securities.Add(Security(key, items, matches[key], master, storedByHolding, newByHolding, actions, firstImportDate));
        }

        securities = [.. securities.OrderBy(s => s.Symbol, StringComparer.Ordinal).ThenBy(s => s.Key, StringComparer.Ordinal)];
        var notHeld = securities.Where(s => s.Rebuild.Status == RebuildStatuses.NoHolding).ToList();
        var unmatched = notHeld.Where(s => !s.Matched).ToList();
        var digest = LotShapes.Digest(securities.Select(s => $"{s.Key}|{s.Rebuild.Digest}").Order(StringComparer.Ordinal));
        return new TradePlan(securities, notHeld.Count, notHeld.Sum(s => s.Trades), unmatched.Count, unmatched.Sum(s => s.Trades), digest);
    }

    private static SecurityPlan Security(
        string key,
        List<FileTrade> items,
        HoldingMatch match,
        TradeInstruments master,
        Dictionary<Guid, List<Transaction>> storedByHolding,
        Dictionary<Guid, List<Transaction>> newByHolding,
        IReadOnlyDictionary<Guid, IReadOnlyList<AppliedAction>> actions,
        DateOnly firstImportDate)
    {
        var first = items[0].Trade;
        var isin = match.Holding?.Isin ?? items.Select(i => i.Trade.Isin).FirstOrDefault(i => i is not null);
        var symbol = match.Holding?.TradingSymbol ?? (first.TradingSymbol.Length > 0 ? first.TradingSymbol : first.Isin ?? string.Empty);
        var buys = items.Count(i => i.Trade.Side == TradeSide.Buy);
        var dates = items.Select(i => i.Trade.TradeDate).ToList();
        HoldingRebuild rebuild;
        var matched = true;
        Guid? holdingId = null;
        switch (match.Kind)
        {
            case MatchKind.One:
                var holding = match.Holding!;
                holdingId = holding.Id;
                var trades = storedByHolding.GetValueOrDefault(holding.Id, []).Concat(newByHolding.GetValueOrDefault(holding.Id, [])).ToList();
                var applied = holding.InstrumentId is { } instrument ? actions.GetValueOrDefault(instrument) ?? [] : [];
                rebuild = HoldingRebuilds.Compute(holding, trades, applied, firstImportDate);
                break;
            case MatchKind.Ambiguous:
                rebuild = HoldingRebuild.Without(RebuildStatuses.Ambiguous, key);
                break;
            default:
                matched = master.Find(items.Select(i => i.Trade.Isin).FirstOrDefault(i => i is not null), first.TradingSymbol) is not null;
                rebuild = HoldingRebuild.Without(RebuildStatuses.NoHolding, key);
                break;
        }

        return new SecurityPlan(key, symbol, isin, matched, holdingId, items.Count, buys, items.Count - buys, dates.Min(), dates.Max(), rebuild);
    }
}
