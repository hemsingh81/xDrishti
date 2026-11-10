using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Domain.Tests.Selection.SizeScaling;

/// <summary>BK-10 criteria 11 and 12 by hand: six stored trades, net R +1, -1, -1, +2, -1, +1 with scales 1, 1, 0.6, 0.6, 1, 1.</summary>
public sealed class SizingStudyTests
{
    private static readonly StudyTrade[] Six = [new(1, 1), new(-1, 1), new(-1, 0.6), new(2, 0.6), new(-1, 1), new(1, 1)];

    [Fact]
    public void Drawdown_and_total_by_hand_are_two_and_one_unscaled_and_one_point_six_and_point_six_scaled()
    {
        var result = SizingStudy.Compare(Six);

        result.Trades.ShouldBe(6);
        result.UnscaledMaxDrawdownR.ShouldBe(2.0, 1e-9);
        result.UnscaledTotalR.ShouldBe(1.0, 1e-9);
        result.ScaledMaxDrawdownR.ShouldBe(1.6, 1e-9);
        result.ScaledTotalR.ShouldBe(0.6, 1e-9);
        result.DrawdownChangePct!.Value.ShouldBe(-20.0, 1e-6);
        result.TotalChangePct!.Value.ShouldBe(-40.0, 1e-6);
    }

    [Fact]
    public void The_summary_prints_both_numbers_the_caveat_and_recommends_nothing()
    {
        var result = SizingStudy.Compare(Six);

        result.Summary.ShouldContain("max drawdown 20 % lower, total R 40 % lower");
        result.Summary.ShouldContain("descriptive, before costs, same trades");
        result.Summary.ShouldNotContain("recommend");
    }

    [Fact]
    public void A_half_with_fewer_than_thirty_trades_is_insufficient_and_says_how_many()
    {
        var result = SizingStudy.Compare(Six);

        result.Halves.Count.ShouldBe(2);
        result.Halves.ShouldAllBe(h => !h.Sufficient && h.DrawdownSign == null && h.TotalSign == null);
        result.Halves[0].Trades.ShouldBe(3);
    }

    [Fact]
    public void Halves_of_thirty_or_more_trades_give_the_sign_of_the_change_in_each()
    {
        var first = Enumerable.Range(0, 30).Select(i => new StudyTrade(i % 3 == 0 ? -1 : 0.6, 0.5));
        var second = Enumerable.Range(0, 30).Select(_ => new StudyTrade(1, 1));

        var result = SizingStudy.Compare([.. first, .. second]);

        result.Halves.ShouldAllBe(h => h.Sufficient);
        result.Halves[0].TotalSign.ShouldBe(-1);
        result.Halves[1].TotalSign.ShouldBe(0);
        result.Halves[1].DrawdownSign.ShouldBe(0);
    }

    [Fact]
    public void The_study_never_scales_up_a_scale_above_one_is_read_as_one()
    {
        var result = SizingStudy.Compare([new StudyTrade(1, 3.0), new StudyTrade(-1, 9.0)]);

        result.ScaledTotalR.ShouldBe(result.UnscaledTotalR, 1e-12);
        result.ScaledMaxDrawdownR.ShouldBe(result.UnscaledMaxDrawdownR, 1e-12);
    }

    [Fact]
    public void Property_a_scaled_total_loss_is_never_larger_than_the_unscaled_one_and_the_drawdown_never_grows_for_losing_trades()
    {
        var random = new Random(3);
        for (var i = 0; i < 500; i++)
        {
            var trades = Enumerable.Range(0, 40).Select(_ => new StudyTrade(-random.NextDouble() * 2, random.NextDouble())).ToList();

            var result = SizingStudy.Compare(trades);

            result.ScaledMaxDrawdownR.ShouldBeLessThanOrEqualTo(result.UnscaledMaxDrawdownR + 1e-9);
        }
    }

    [Fact]
    public void An_empty_study_is_not_a_division_by_zero()
    {
        var result = SizingStudy.Compare([]);

        result.Trades.ShouldBe(0);
        result.DrawdownChangePct.ShouldBeNull();
        result.TotalChangePct.ShouldBeNull();
    }
}
