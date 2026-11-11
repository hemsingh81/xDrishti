using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.History;

namespace XDrishti.Domain.Tests.Market.History;

/// <summary>CF-04 Part B criterion 8 and review fix 3: the factor by the bar's date against the seam.</summary>
public sealed class SourceAwareFactorsTests
{
    private static readonly DateOnly Seam = new(2024, 4, 2);

    private static readonly DateOnly Through = new(2026, 4, 30);

    private static AppliedAction Split(string exDate, int numerator, int denominator) =>
        new(DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), CorporateActionKind.Split, numerator, denominator, CorporateActionRatio.Factor(CorporateActionKind.Split, numerator, denominator));

    private static AppliedAction Bonus(string exDate) =>
        new(DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), CorporateActionKind.Bonus, 1, 1, CorporateActionRatio.Factor(CorporateActionKind.Bonus, 1, 1));

    private static SourceAwareFactors Hand(params AppliedAction[] extra) =>
        new(AdjustmentFactors.From([Split("2023-03-10", 1, 5), Bonus("2024-08-01"), .. extra]), Seam, Through);

    [Fact]
    public void A_raw_dhan_close_before_the_seam_takes_every_later_action_including_those_inside_the_csv_span()
    {
        var factors = Hand();

        factors.FactorAt(new DateOnly(2022, 12, 1)).ShouldBe(0.1, 1e-12);
        factors.Price(2500.00, new DateOnly(2022, 12, 1)).ShouldBe(250.00);
    }

    [Fact]
    public void A_csv_bar_takes_no_factor_from_an_action_inside_the_file_and_stays_as_stored()
    {
        var factors = Hand();

        factors.FactorAt(new DateOnly(2024, 5, 2)).ShouldBe(1.0);
        factors.Price(300.00, new DateOnly(2024, 5, 2)).ShouldBe(300.00);
    }

    [Fact]
    public void A_later_split_after_the_csv_end_applies_to_both_the_dhan_and_the_csv_bar_and_volume_is_divided_by_the_same_factors()
    {
        var factors = Hand(Split("2026-06-15", 1, 2));

        factors.Price(2500.00, new DateOnly(2022, 12, 1)).ShouldBe(125.00);
        factors.Price(300.00, new DateOnly(2024, 5, 2)).ShouldBe(150.00);
        factors.Volume(1_000, new DateOnly(2022, 12, 1)).ShouldBe(20_000);
        factors.Volume(1_000, new DateOnly(2024, 5, 2)).ShouldBe(2_000);
    }

    [Fact]
    public void A_dhan_bar_on_the_ex_date_takes_no_factor_from_that_action_and_the_day_before_does()
    {
        var factors = new SourceAwareFactors(AdjustmentFactors.From([Bonus("2024-08-01")]), new DateOnly(2024, 8, 5), null);

        factors.Price(400.00, new DateOnly(2024, 7, 31)).ShouldBe(200.00);
        factors.Price(205.00, new DateOnly(2024, 8, 1)).ShouldBe(205.00);
    }

    [Fact]
    public void Without_a_recorded_csv_basis_every_bar_takes_every_later_action()
    {
        var factors = new SourceAwareFactors(AdjustmentFactors.From([Bonus("2024-08-01")]), Seam, null);

        factors.FactorAt(new DateOnly(2024, 5, 2)).ShouldBe(0.5);
    }
}
