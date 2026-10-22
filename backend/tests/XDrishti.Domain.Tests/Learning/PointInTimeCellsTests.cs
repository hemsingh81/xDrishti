using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

public sealed class PointInTimeCellsTests
{
    private static readonly CellId Cell = new("ORB", "15m", "BUY", "intraday", "FIXED_RR");
    private static readonly CellStatsOptions Options = new(PriorStrength: 2, MinTrades: 3);

    private static CellTrade Win(DateOnly exit) => new(exit, 1.0, 0.2, 1.5);

    private static CellTrade Loss(DateOnly exit) => new(exit, -1.0, 1.0, 0.1);

    private static IReadOnlyDictionary<string, double[]> Ask(IReadOnlyList<CellTrade> trades, DateOnly decision) =>
        PointInTimeCells.Compute([new CellInput(Cell, false, trades)], [new CellAsk("t", Cell.Key, decision)], Options);

    [Fact]
    public void A_trade_that_exited_the_day_before_the_decision_counts_and_one_that_exits_on_the_decision_day_does_not()
    {
        var decision = new DateOnly(2025, 3, 10);
        var trades = new[] { Win(new DateOnly(2025, 3, 7)), Win(new DateOnly(2025, 3, 9)), Loss(decision), Loss(decision.AddDays(1)) };

        var values = Ask(trades, decision)["t"];

        values[0].ShouldBe(2, "only the two exits before the decision date are known");
        values[3].ShouldBe(1.0, "their expectancy is +1R");
    }

    [Fact]
    public void Changing_or_adding_trades_that_exit_on_or_after_the_decision_date_changes_nothing()
    {
        var decision = new DateOnly(2025, 3, 10);
        var known = new[] { Win(new DateOnly(2025, 3, 7)), Loss(new DateOnly(2025, 3, 8)) };
        var later = known.Concat([Win(decision), Loss(decision.AddDays(30)), Win(decision.AddDays(200))]).ToArray();

        var before = Ask(known, decision)["t"];
        var after = Ask(later, decision)["t"];

        after.ShouldBe(before);
    }

    [Fact]
    public void A_cell_with_no_trade_yet_reads_n_zero_and_the_prior_with_an_unknown_expectancy()
    {
        var values = Ask([Win(new DateOnly(2025, 3, 20))], new DateOnly(2025, 3, 10))["t"];

        values[0].ShouldBe(0);
        values[1].ShouldBe(0.5);
        double.IsNaN(values[3]).ShouldBeTrue();
        values[4].ShouldBe(0);
    }

    [Fact]
    public void The_enough_flag_turns_on_at_the_minimum_number_of_trades()
    {
        var trades = new[] { Win(new DateOnly(2025, 3, 3)), Loss(new DateOnly(2025, 3, 4)), Win(new DateOnly(2025, 3, 5)) };

        Ask(trades, new DateOnly(2025, 3, 5))["t"][4].ShouldBe(0, "two trades are known on the 5th");
        Ask(trades, new DateOnly(2025, 3, 6))["t"][4].ShouldBe(1, "three are known on the 6th");
    }

    [Fact]
    public void A_cell_the_calculator_does_not_know_is_all_unknown()
    {
        var result = PointInTimeCells.Compute([new CellInput(Cell, false, [Win(new DateOnly(2025, 3, 3))])], [new CellAsk("t", "OTHER|15m|BUY|intraday|X|*", new DateOnly(2025, 3, 10))], Options);

        result["t"].ShouldAllBe(v => double.IsNaN(v));
    }

    [Fact]
    public void Several_decisions_on_different_dates_each_see_only_their_own_past()
    {
        var trades = new[] { Win(new DateOnly(2025, 3, 3)), Loss(new DateOnly(2025, 3, 5)), Win(new DateOnly(2025, 3, 7)) };

        var result = PointInTimeCells.Compute(
            [new CellInput(Cell, false, trades)],
            [new CellAsk("a", Cell.Key, new DateOnly(2025, 3, 4)), new CellAsk("b", Cell.Key, new DateOnly(2025, 3, 6)), new CellAsk("c", Cell.Key, new DateOnly(2025, 3, 8))],
            Options);

        result["a"][0].ShouldBe(1);
        result["b"][0].ShouldBe(2);
        result["c"][0].ShouldBe(3);
    }
}
