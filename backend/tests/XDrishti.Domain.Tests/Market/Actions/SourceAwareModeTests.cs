using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.History;

namespace XDrishti.Domain.Tests.Market.Actions;

/// <summary>CF-04d decision 5 (acceptance criterion 1): the source-aware mode of AdjustmentFactors gives the factors of SourceAwareFactors, the oracle, on every date.</summary>
public sealed class SourceAwareModeTests
{
    private static readonly DateOnly Seam = new(2024, 4, 2);

    private static readonly DateOnly Through = new(2026, 4, 30);

    private static AppliedAction Split(string exDate, int numerator, int denominator) =>
        new(DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), CorporateActionKind.Split, numerator, denominator, CorporateActionRatio.Factor(CorporateActionKind.Split, numerator, denominator));

    private static AppliedAction Bonus(string exDate) =>
        new(DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), CorporateActionKind.Bonus, 1, 1, CorporateActionRatio.Factor(CorporateActionKind.Bonus, 1, 1));

    private static AdjustmentFactors Plain(params AppliedAction[] extra) => AdjustmentFactors.From([Split("2023-03-10", 1, 5), Bonus("2024-08-01"), .. extra]);

    [Fact]
    public void The_hand_case_prices_are_the_ones_of_the_spec()
    {
        var factors = Plain().SourceAware(Seam, Through);

        AdjustmentFactors.Price(2500.00, factors.FactorAt(new DateOnly(2022, 12, 1))).ShouldBe(250.00);
        AdjustmentFactors.Volume(1_000, factors.FactorAt(new DateOnly(2022, 12, 1))).ShouldBe(10_000);
        AdjustmentFactors.Price(500.00, factors.FactorAt(new DateOnly(2024, 4, 1))).ShouldBe(250.00);
        AdjustmentFactors.Price(252.10, factors.FactorAt(new DateOnly(2024, 4, 2))).ShouldBe(252.10);
        AdjustmentFactors.Price(260.00, factors.FactorAt(new DateOnly(2024, 5, 2))).ShouldBe(260.00);
        AdjustmentFactors.Price(260.00, Plain().FactorAt(new DateOnly(2024, 5, 2))).ShouldBe(130.00, "the plain factors would halve the CSV bar a second time");
    }

    [Fact]
    public void A_split_after_the_csv_end_gives_125_and_130_and_volume_times_20()
    {
        var factors = Plain(Split("2026-06-15", 1, 2)).SourceAware(Seam, Through);

        AdjustmentFactors.Price(2500.00, factors.FactorAt(new DateOnly(2022, 12, 1))).ShouldBe(125.00);
        AdjustmentFactors.Price(260.00, factors.FactorAt(new DateOnly(2024, 5, 2))).ShouldBe(130.00);
        AdjustmentFactors.Volume(1_000, factors.FactorAt(new DateOnly(2022, 12, 1))).ShouldBe(20_000);
    }

    [Fact]
    public void Actions_and_actions_after_are_those_of_the_plain_factors()
    {
        var plain = Plain();
        var aware = plain.SourceAware(Seam, Through);

        aware.Actions.ShouldBe(plain.Actions);
        aware.ActionsAfter(new DateOnly(2024, 4, 1)).ShouldBe(plain.ActionsAfter(new DateOnly(2024, 4, 1)));
        AdjustmentFactors.None.SourceAware(Seam, Through).FactorAt(new DateOnly(2022, 1, 1)).ShouldBe(1.0);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void On_every_date_from_2021_to_2026_the_mode_equals_the_oracle(bool withLateSplit, bool withThrough)
    {
        var plain = withLateSplit ? Plain(Split("2026-06-15", 1, 2), Split("2025-02-03", 2, 1)) : Plain();
        DateOnly? through = withThrough ? Through : null;
        var aware = plain.SourceAware(Seam, through);
        var oracle = new SourceAwareFactors(plain, Seam, through);

        for (var date = new DateOnly(2021, 1, 1); date <= new DateOnly(2026, 12, 31); date = date.AddDays(1))
        {
            aware.FactorAt(date).ShouldBe(oracle.FactorAt(date), 1e-12, date.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
