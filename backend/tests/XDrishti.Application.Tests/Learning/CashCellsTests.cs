using Microsoft.Extensions.Logging;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>P14-06 criterion 6 (ADR 0193): the decision paths read cash cells only; a non-cash row is left out and counted, never an error.</summary>
public sealed class CashCellsTests
{
    internal static StoredCellStats Row(Expression expression, double posterior = 0.55)
    {
        var cell = new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", Expression: expression);
        var row = new CellStatsRow(cell, CellWindow.All, new DateOnly(2026, 10, 23), true, 40, 20, 40, 0.5, 0.5, posterior, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new StoredCellStats("snap", DateTimeOffset.UnixEpoch, new CellStatsOptions(), "sources", row);
    }

    [Fact]
    public void Only_cash_rows_stay_in_their_order_and_the_others_are_counted_in_the_log()
    {
        var cash = Row(Expression.Cash);
        var option = Row(Expression.OptionBuy, 0.9);
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var kept = CashCells.Only([option, cash, Row(Expression.Pair)], "plan", logger);

        kept.ShouldBe([cash]);
        logger.ReceivedWithAnyArgs(1).Log(default, default, default(object), default, default!);
    }

    [Fact]
    public void A_list_of_cash_rows_is_returned_as_it_is_and_logs_nothing()
    {
        var rows = new[] { Row(Expression.Cash) };
        var logger = Substitute.For<ILogger>();

        CashCells.Only(rows, "plan", logger).ShouldBeSameAs(rows);
        CashCells.Only([], "plan", logger).ShouldBeEmpty();
        logger.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void The_counter_has_the_name_the_spec_gives()
    {
        CashCells.SkippedCode.ShouldBe("non_cash_cell_skipped");
    }
}
