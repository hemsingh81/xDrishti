using XDrishti.Domain.Market;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The paper ledger of one account, derived from its stored trades and nothing else (spec P10-02, ADR 0118). Pure and order-independent: the same trades in any order give the same figures. Equity is capital plus the realised result (open gains
/// are shown beside it, never inside the sizing base); there is no leverage, so funds are equity less the open notional. Nothing here stores, places or moves anything.
/// </summary>
public static class PaperLedger
{
    /// <summary>The figures of the account's trades. <paramref name="capital"/> null means the account has no capital figure (BrokerFunds). <paramref name="lastSession"/> is the last closed session (null: no last-session figure).</summary>
    public static LedgerFigures Compute(decimal? capital, IReadOnlyList<LedgerTrade> trades, DateOnly? lastSession)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var closed = Closed(trades);
        var open = trades.Where(t => !t.Closed).ToList();
        var gross = closed.Sum(t => t.Exit!.GrossPnl);
        var charged = closed.Where(t => t.Exit!.Charged).ToList();
        decimal? charges = charged.Count == 0 ? null : charged.Sum(t => t.Exit!.Charges ?? 0m);
        var result = closed.Sum(t => t.Exit!.Result);
        var basis = charged.Count == 0 ? PaperWording.Gross : charged.Count == closed.Count ? PaperWording.AfterStoredSheet : PaperWording.Mixed(charged.Count, closed.Count);

        decimal? equity = capital is { } c ? c + result : null;
        var notional = open.Sum(t => t.Notional);
        var risk = open.Sum(t => t.RiskAmount);

        decimal lastResult = 0m;
        decimal? lastPct = null;
        if (lastSession is { } last)
        {
            lastResult = closed.Where(t => IndiaTime.Today(t.Exit!.ExitAt) == last).Sum(t => t.Exit!.Result);
            var before = closed.Where(t => IndiaTime.Today(t.Exit!.ExitAt) < last).Sum(t => t.Exit!.Result);
            lastPct = capital is { } start && start + before > 0 ? lastResult / (start + before) * 100m : null;
        }

        decimal running = 0m, peak = 0m, worst = 0m, worstBase = 0m;
        foreach (var t in closed)
        {
            running += t.Exit!.Result;
            peak = Math.Max(peak, running);
            if (peak - running > worst)
            {
                worst = peak - running;
                worstBase = peak;
            }
        }

        decimal? drawdownPct = capital is { } cap && cap + worstBase > 0 ? worst / (cap + worstBase) * 100m : null;
        return new LedgerFigures(
            gross, charges, result, basis, closed.Count, open.Count, equity, notional,
            equity is { } e ? Math.Max(0m, e - notional) : null,
            risk, equity is > 0 ? risk / equity.Value * 100m : null,
            lastResult, lastPct, worst, drawdownPct);
    }

    /// <summary>The realised result after each India date's exits, for the dates asked: <c>capital + result of exits on or before the date</c>. Null equity without capital.</summary>
    public static IReadOnlyList<(DateOnly Date, decimal Result, decimal? Equity)> RealisedSeries(decimal? capital, IReadOnlyList<LedgerTrade> trades, IReadOnlyList<DateOnly> dates)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(dates);
        var closed = Closed(trades);
        return [.. dates.Select(d =>
        {
            var result = closed.Where(t => IndiaTime.Today(t.Exit!.ExitAt) <= d).Sum(t => t.Exit!.Result);
            return (d, result, capital is { } c ? (decimal?)(c + result) : null);
        })];
    }

    /// <summary>The sum of the open positions' unrealised rupees; null when any mark is unavailable (the sum of the others is not the account's figure) and null when there is no position.</summary>
    public static decimal? TotalUnrealised(IReadOnlyList<PaperPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.Count == 0 || positions.Any(p => p.Unrealised is null) ? null : positions.Sum(p => p.Unrealised!.Value);
    }

    /// <summary>The closed trades in the one deterministic order: exit instant, then count-once key, account, entry id.</summary>
    public static List<LedgerTrade> Closed(IReadOnlyList<LedgerTrade> trades) =>
        [.. trades.Where(t => t.Closed).OrderBy(t => t.Exit!.ExitAt).ThenBy(t => t.TradeKey, StringComparer.Ordinal).ThenBy(t => t.AccountId).ThenBy(t => t.EntryId)];
}
