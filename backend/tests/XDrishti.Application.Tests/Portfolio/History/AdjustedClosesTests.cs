using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Market.Actions;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>The stored closes adjusted for the applied corporate actions on read (ADR 0017 §5).</summary>
public sealed class AdjustedClosesTests
{
    private static StoredClose Raw(int day, double close) => new(Oct(day), close);

    [Fact]
    public void Closes_before_the_ex_date_are_multiplied_by_the_factor_and_later_ones_are_not()
    {
        var split = new AppliedAction(Oct(7), CorporateActionKind.Split, 1, 2, 0.5m);

        var adjusted = AdjustedCloses.Adjust([Raw(5, 200.0), Raw(6, 210.0), Raw(7, 105.0), Raw(8, 110.0)], AdjustmentFactors.From([split]));

        adjusted.Select(a => a.Close).ShouldBe([100m, 105m, 105m, 110m]);
    }

    [Fact]
    public void A_bonus_and_a_split_multiply()
    {
        var split = new AppliedAction(Oct(8), CorporateActionKind.Split, 1, 2, 0.5m);
        var bonus = new AppliedAction(Oct(7), CorporateActionKind.Bonus, 1, 1, 0.5m);

        var adjusted = AdjustedCloses.Adjust([Raw(6, 400.0), Raw(7, 200.0), Raw(8, 100.0)], AdjustmentFactors.From([split, bonus]));

        adjusted.Select(a => a.Close).ShouldBe([100m, 100m, 100m]);
    }

    [Fact]
    public void Without_actions_the_closes_are_unchanged_and_rounded_to_four_decimals()
    {
        var adjusted = AdjustedCloses.Adjust([Raw(5, 100.12345678)], AdjustmentFactors.None);

        adjusted.Single().Close.ShouldBe(100.1235m);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_close_that_is_not_a_positive_number_is_dropped(double close)
    {
        AdjustedCloses.Adjust([Raw(5, close), Raw(6, 10.0)], AdjustmentFactors.None).Select(a => a.Date.Day).ShouldBe([6]);
    }

    [Fact]
    public void The_result_is_in_date_order()
    {
        AdjustedCloses.Adjust([Raw(7, 3.0), Raw(5, 1.0), Raw(6, 2.0)], AdjustmentFactors.None).Select(a => a.Date.Day).ShouldBe([5, 6, 7]);
    }
}
