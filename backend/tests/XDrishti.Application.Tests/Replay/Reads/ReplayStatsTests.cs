using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay.Reads;

namespace XDrishti.Application.Tests.Replay.Reads;

/// <summary>SR-01 part C: the figures of a run from its stored equity and trades.</summary>
public sealed class ReplayStatsTests
{
    private static readonly DateOnly D1 = new(2026, 10, 5);

    private static ReplayTradeRow Trade(long no, decimal? net, double? r, string cell = "S1|1D|BUY|swing|FIXED_RR", bool closed = true) => new(
        no, "AAA", "BUY", cell, 10, D1, D1.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), 100m, 98m, true, "k" + no, closed ? D1.AddDays(1) : null, null, net, 0m, net, null, r);

    [Fact]
    public void No_stored_session_has_no_figures()
    {
        ReplayStats.Figures(1_000_000m, D1, [], null, []).ShouldBeNull();
    }

    [Fact]
    public void Figures_come_from_the_equity_series_and_the_closed_trades()
    {
        var days = new[] { (D1, 1_010_000m, 1), (D1.AddDays(1), 1_005_000m, 0), (D1.AddDays(2), 1_020_000m, 1) };
        var trades = new[] { Trade(1, 120m, 1.2), Trade(2, -50m, -0.5), Trade(3, null, null, closed: false) };

        var f = ReplayStats.Figures(1_000_000m, D1, days, 0.5m, trades)!;

        f.FinalEquity.ShouldBe(1_020_000m);
        f.ReturnPct.ShouldBe(2m);
        f.Exposure!.Value.ShouldBe(2 / 3.0, 1e-9);
        (f.Trades, f.ClosedTrades).ShouldBe((3, 2));
        f.HitRate!.Value.ShouldBe(0.5);
        f.MeanR!.Value.ShouldBe(0.35, 1e-9);
        f.Sharpe.ShouldNotBeNull();
        f.Cagr.ShouldNotBeNull();
    }

    [Fact]
    public void One_flat_day_has_no_sharpe_and_no_cagr()
    {
        var f = ReplayStats.Figures(1_000_000m, D1, [(D1, 1_000_000m, 0)], null, [])!;

        f.Sharpe.ShouldBeNull();
        f.Cagr.ShouldBeNull();
        f.HitRate.ShouldBeNull();
    }

    [Fact]
    public void Contribution_groups_closed_trades_by_strategy()
    {
        var trades = new[] { Trade(1, 100m, 1), Trade(2, 50m, 0.5), Trade(3, -20m, -1, "S2|1D|BUY|intraday|FIXED_RR") };

        var c = ReplayStats.Contribution(trades);

        c.Select(x => (x.Strategy, x.Trades, x.Result)).ShouldBe([("S1", 2, 150m), ("S2", 1, -20m)]);
    }

    [Fact]
    public void Drawdown_is_measured_from_the_peak_with_the_capital_as_the_first_peak()
    {
        ReplayStats.Drawdowns(100m, [90m, 110m, 99m]).ShouldBe([10m, 0m, 10m]);
    }
}
