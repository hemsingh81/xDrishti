using XDrishti.Domain.Learning;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>Builders for the selection tests: a candidate that passes every filter, changed one number at a time.</summary>
internal static class Candidates
{
    public static readonly DateOnly Decision = new(2026, 10, 26);

    public static CellStatsRow Stats(
        string strategy = "S1",
        int n = 40,
        double posterior = 0.55,
        double lower = 0.50,
        double? avgWin = 2.0,
        double? avgLoss = -1.0,
        bool costed = true,
        DateOnly? asOf = null) =>
        new(
            new CellId(strategy, "15m", "BUY", "intraday", "fixed"),
            CellWindow.All,
            asOf ?? new DateOnly(2026, 10, 23),
            costed,
            n,
            n / 2,
            n,
            0.5,
            0.5,
            posterior,
            lower,
            0.6,
            avgWin,
            avgLoss,
            0.5,
            1.5,
            3,
            0.3,
            0.8,
            0.5,
            1.5,
            true);

    /// <summary>Conservative expectancy of the default stats: 0.5 * 2 + 0.5 * -1 = 0.5 R.</summary>
    public static PlanCandidate Good(
        string symbol = "AAA",
        string strategy = "S1",
        string side = "BUY",
        string timeframe = "15m",
        CellState? state = CellState.Candidate,
        CellStatsRow? stats = null,
        double? rr = 2.0,
        double? cost = 0.05,
        double? turnover = 50_000_000,
        bool? eventBlock = false,
        bool? ban = false,
        bool? surveillance = false,
        double? regime = null,
        double? corr = null,
        double? eventRisk = null,
        double? advisory = null)
    {
        var cell = new CellId(strategy, timeframe, side, "intraday", "fixed");
        return new PlanCandidate(symbol, cell, state, stats ?? Stats(strategy), Decision, rr, cost, turnover, eventBlock, ban, surveillance, regime, corr, eventRisk, advisory);
    }
}
