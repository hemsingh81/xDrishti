using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The cell statistics against hand tables (P6-01, ADR 0064). Half-life 30 days makes the weights 1, 0.5, 0.25 at 0, 30 and 60 days.</summary>
public sealed class CellStatsCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 31);

    private static readonly CellStatsOptions NoPrior = new(RecencyHalfLifeDays: 30, PriorStrength: 0, MinTrades: 3);

    private static CellId Cell(string strategy = "ORB") => new(strategy, "15m", "BUY", "intraday", "FIXED_RR");

    private static CellTrade Trade(int daysBefore, double r, double mae = 0, double mfe = 0) => new(AsOf.AddDays(-daysBefore), r, mae, mfe);

    private static CellStatsRow Row(IReadOnlyList<CellStatsRow> rows, CellWindow window, int index = 0) => rows.Where(r => r.Window == window).ElementAt(index);

    [Fact]
    public void A_weight_that_underflows_to_zero_gives_unknown_values_not_NaN()
    {
        var input = new CellInput(Cell(), false, [Trade(3000, 1), Trade(3001, -1)]);

        var all = Row(CellStatsCalculator.Compute([input], AsOf, new CellStatsOptions(RecencyHalfLifeDays: 1, PriorStrength: 0, MinTrades: 1)), CellWindow.All);

        all.N.ShouldBe(2);
        all.ExpectancyR.ShouldBeNull();
        all.ProfitFactor.ShouldBeNull();
        double.IsFinite(all.WinRatePosterior).ShouldBeTrue();
    }

    [Fact]
    public void Hand_table_with_no_prior_and_recency_weights()
    {
        var input = new CellInput(Cell(), true, [Trade(60, 1, 2, 0.5), Trade(0, 2, 0.5, 3), Trade(30, -1, 1, 1)]);

        var all = Row(CellStatsCalculator.Compute([input], AsOf, NoPrior), CellWindow.All);

        all.N.ShouldBe(3);
        all.Wins.ShouldBe(2);
        all.EffectiveN.ShouldBe(1.75, 1e-12);
        all.WinRateRaw!.Value.ShouldBe(2.0 / 3, 1e-12);
        all.WinRatePosterior.ShouldBe(1.25 / 1.75, 1e-12);
        all.ExpectancyR!.Value.ShouldBe(1.0, 1e-12);
        all.AvgWinR!.Value.ShouldBe(1.8, 1e-12);
        all.AvgLossR!.Value.ShouldBe(-1.0, 1e-12);
        all.ProfitFactor!.Value.ShouldBe(4.5, 1e-12);
        all.MaxDrawdownR.ShouldBe(1.0, 1e-12);
        all.MaeP50!.Value.ShouldBe(1.0, 1e-12);
        all.MaeP90!.Value.ShouldBe(1.8, 1e-12);
        all.MfeP50!.Value.ShouldBe(1.0, 1e-12);
        all.Enough.ShouldBeTrue();
        all.Costed.ShouldBeTrue();
        all.WinRateLower.ShouldBeLessThan(all.WinRatePosterior);
        all.WinRateUpper.ShouldBeGreaterThan(all.WinRatePosterior);
    }

    [Fact]
    public void Credible_bounds_of_a_known_beta()
    {
        // two wins and a loss at weight 1, strength 0 -> Beta(2, 1), whose CDF is x squared
        var input = new CellInput(Cell(), false, [Trade(0, 1), Trade(0, 1), Trade(0, -1)]);

        var row = Row(CellStatsCalculator.Compute([input], AsOf, new CellStatsOptions(30, 0, 1, 0.90)), CellWindow.All);

        row.WinRateLower.ShouldBe(Math.Sqrt(0.05), 1e-8);
        row.WinRateUpper.ShouldBe(Math.Sqrt(0.95), 1e-8);
        row.WinRatePosterior.ShouldBe(2.0 / 3, 1e-12);
    }

    [Fact]
    public void Windows_cut_at_250_and_60_days_with_the_older_edge_excluded()
    {
        var input = new CellInput(Cell(), true, [Trade(0, 1), Trade(59, 1), Trade(60, 1), Trade(249, 1), Trade(250, 1), Trade(400, -1)]);

        var rows = CellStatsCalculator.Compute([input], AsOf, NoPrior);

        Row(rows, CellWindow.All).N.ShouldBe(6);
        Row(rows, CellWindow.Days250).N.ShouldBe(4);
        Row(rows, CellWindow.Days60).N.ShouldBe(2);
        rows.Count.ShouldBe(3);
    }

    [Fact]
    public void A_trade_that_exits_after_the_as_of_date_is_never_read()
    {
        var input = new CellInput(Cell(), true, [Trade(0, 1), Trade(-1, -5), Trade(-30, -5)]);

        var all = Row(CellStatsCalculator.Compute([input], AsOf, NoPrior), CellWindow.All);

        all.N.ShouldBe(1);
        all.ExpectancyR!.Value.ShouldBe(1.0, 1e-12);
    }

    [Fact]
    public void Prior_is_the_strategy_group_two_cells_of_one_strategy()
    {
        // system 0.5, strategy 0.5 ((10 + 10*0.5)/(20+10) = 0.5); A: (5+10)/20 = 0.75, B: 5/20 = 0.25
        var a = new CellInput(Cell() with { Side = "BUY" }, true, [.. Enumerable.Range(0, 10).Select(_ => Trade(0, 1))]);
        var b = new CellInput(Cell() with { Side = "SELL" }, true, [.. Enumerable.Range(0, 10).Select(_ => Trade(0, -1))]);

        var rows = CellStatsCalculator.Compute([a, b], AsOf, new CellStatsOptions(30, 10, 3, 0.9));

        Row(rows, CellWindow.All, 0).PriorRate.ShouldBe(0.5, 1e-12);
        Row(rows, CellWindow.All, 0).WinRatePosterior.ShouldBe(0.75, 1e-12);
        Row(rows, CellWindow.All, 1).WinRatePosterior.ShouldBe(0.25, 1e-12);
    }

    [Fact]
    public void Prior_is_the_strategy_shrunk_to_the_system_two_strategies()
    {
        // system 0.5; X: (10 + 5)/20 = 0.75 -> A: (7.5 + 10)/20 = 0.875; Y: 5/20 = 0.25 -> B: 2.5/20 = 0.125
        var a = new CellInput(Cell("X"), true, [.. Enumerable.Range(0, 10).Select(_ => Trade(0, 1))]);
        var b = new CellInput(Cell("Y"), true, [.. Enumerable.Range(0, 10).Select(_ => Trade(0, -1))]);

        var rows = CellStatsCalculator.Compute([a, b], AsOf, new CellStatsOptions(30, 10, 3, 0.9));

        Row(rows, CellWindow.All, 0).PriorRate.ShouldBe(0.75, 1e-12);
        Row(rows, CellWindow.All, 0).WinRatePosterior.ShouldBe(0.875, 1e-12);
        Row(rows, CellWindow.All, 1).WinRatePosterior.ShouldBe(0.125, 1e-12);
    }

    [Fact]
    public void An_empty_cell_reads_the_prior_and_not_enough_evidence()
    {
        var a = new CellInput(Cell("X"), false, [Trade(0, 1), Trade(0, 1)]);
        var empty = new CellInput(Cell("X") with { Side = "SELL" }, false, []);

        var row = Row(CellStatsCalculator.Compute([a, empty], AsOf, new CellStatsOptions(30, 10, 3, 0.9)), CellWindow.All, 1);

        row.N.ShouldBe(0);
        row.Enough.ShouldBeFalse();
        row.Costed.ShouldBeFalse();
        row.WinRateRaw.ShouldBeNull();
        row.ExpectancyR.ShouldBeNull();
        row.ProfitFactor.ShouldBeNull();
        row.MaeP50.ShouldBeNull();
        row.WinRatePosterior.ShouldBe(row.PriorRate, 1e-12);
        row.MaxDrawdownR.ShouldBe(0);
    }

    [Fact]
    public void A_zero_result_is_not_a_win_and_no_loss_gives_no_profit_factor()
    {
        var input = new CellInput(Cell(), true, [Trade(0, 0), Trade(0, 2)]);

        var row = Row(CellStatsCalculator.Compute([input], AsOf, NoPrior), CellWindow.All);

        row.Wins.ShouldBe(1);
        row.AvgLossR.ShouldBeNull();
        row.ProfitFactor.ShouldBeNull();
    }

    [Fact]
    public void Drawdown_follows_the_order_of_the_exits()
    {
        var input = new CellInput(Cell(), true, [Trade(0, -1), Trade(3, 2), Trade(2, -1.5), Trade(1, -0.5)]);

        var row = Row(CellStatsCalculator.Compute([input], AsOf, NoPrior), CellWindow.All);

        row.MaxDrawdownR.ShouldBe(3.0, 1e-12); // +2, +0.5, 0, then -1: peak 2 to trough -1
    }

    [Fact]
    public void Unusable_options_throw_and_are_listed()
    {
        new CellStatsOptions().Problems().ShouldBeEmpty();
        new CellStatsOptions(0, -1, 0, 1).Problems().Count.ShouldBe(4);
        Should.Throw<ArgumentException>(() => CellStatsCalculator.Compute([], AsOf, new CellStatsOptions(0)));
    }

    [Fact]
    public void Cell_key_and_parts()
    {
        Cell().Key.ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|*");
        (Cell() with { Exit = "a|b" }).Problems().Count.ShouldBe(1);
        CellWindowExtensions.Parse("60d").ShouldBe(CellWindow.Days60);
        CellWindowExtensions.Parse("x").ShouldBeNull();
        CellWindow.Days250.Code().ShouldBe("250d");
    }
}
