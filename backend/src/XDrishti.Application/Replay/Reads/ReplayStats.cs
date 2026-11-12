using XDrishti.Application.Abstractions.Replay;

namespace XDrishti.Application.Replay.Reads;

/// <summary>The figures of a run from what is stored (SR-01 part C): equity comes from the stored sessions (the ledger's), the trade figures from the stored trades. Pure.</summary>
internal static class ReplayStats
{
    private const double TradingDays = 252;

    /// <summary>The figures; null when no session is stored. The final equity, the maximum drawdown and the closed-trade count are the ledger's (<see cref="ReplaySummary"/>, <c>PaperLedger</c>, AC 7); the daily series is the stored one.</summary>
    public static ReplayFigures? Figures(decimal capital, DateOnly from, IReadOnlyList<ReplayEquityRow> days, ReplaySummary? summary, IReadOnlyList<ReplayTradeRow> trades)
    {
        if (days.Count == 0 || capital <= 0)
        {
            return null;
        }

        var final = summary?.Figures.Equity ?? days[^1].Equity;
        var years = (days[^1].Session.DayNumber - from.DayNumber + 1) / 365.25;
        double? cagr = days.Count < 2 || years <= 0 || final <= 0 ? null : Math.Pow((double)(final / capital), 1 / years) - 1;

        var returns = new List<double>(days.Count);
        var before = capital;
        foreach (var d in days)
        {
            returns.Add((double)((d.Equity - before) / before));
            before = d.Equity;
        }

        double? sharpe = null;
        if (returns.Count >= 2)
        {
            var mean = returns.Average();
            var sd = Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1));
            sharpe = sd > 0 ? mean / sd * Math.Sqrt(TradingDays) : null;
        }

        var closed = trades.Where(t => t.ExitSession is not null).ToList();
        double? hit = closed.Count == 0 ? null : closed.Count(t => (t.NetPnl ?? t.GrossPnl ?? 0m) > 0) / (double)closed.Count;
        var withR = closed.Where(t => t.RSim is not null).ToList();
        double? meanR = withR.Count == 0 ? null : withR.Average(t => t.RSim!.Value);
        return new ReplayFigures(capital, final, Math.Round((final - capital) / capital * 100, 4), cagr, summary?.Figures.MaxDrawdownPct ?? days[^1].Drawdown, sharpe, days.Count(d => d.OpenPositions > 0) / (double)days.Count, trades.Count, closed.Count, hit, meanR);
    }

    /// <summary>The result of the closed trades per strategy, by strategy name.</summary>
    public static IReadOnlyList<ReplayStrategyContribution> Contribution(IReadOnlyList<ReplayTradeRow> trades) =>
        [.. trades.Where(t => t.ExitSession is not null)
            .GroupBy(t => ReplayReadSupport.Strategy(t.CellKey), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ReplayStrategyContribution(g.Key, g.Count(), g.Sum(t => t.NetPnl ?? t.GrossPnl ?? 0m), g.Sum(t => t.RSim ?? 0)))];

    /// <summary>The trades open at the close of <paramref name="session"/>.</summary>
    public static IEnumerable<ReplayTradeRow> OpenAt(IEnumerable<ReplayTradeRow> trades, DateOnly session) =>
        trades.Where(t => t.EntrySession <= session && (t.ExitSession is null || t.ExitSession > session));

    /// <summary>The drawdown from the peak so far in percent, the capital counting as the first peak.</summary>
    public static IReadOnlyList<decimal> Drawdowns(decimal capital, IReadOnlyList<decimal> equities)
    {
        var peak = capital;
        var result = new List<decimal>(equities.Count);
        foreach (var e in equities)
        {
            peak = Math.Max(peak, e);
            result.Add(peak <= 0 ? 0m : Math.Round((peak - e) / peak * 100, 4));
        }

        return result;
    }
}
