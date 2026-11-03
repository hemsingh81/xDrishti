using XDrishti.Domain.Options.Iv;
using XDrishti.Domain.Options.Pricing;

namespace XDrishti.Domain.Tests.Options.Iv;

/// <summary>P14-11: the daily ATM IV is the mean of the CE and PE offset-0 IV of the last bar of a session, in fractions; a session with one side only, an invalid IV or an unusable key-session has no value.</summary>
public sealed class DailyAtmIvTests
{
    private static readonly DateOnly D1 = new(2026, 7, 28);

    private static readonly DateOnly D2 = new(2026, 7, 29);

    [Fact]
    public void The_unit_label_and_the_bound_are_those_of_the_pricing_core_which_the_iv_types_may_not_name()
    {
        IvLabels.Unit.ShouldBe(PricingLabels.IvUnitPercentAssumed);
        IvLabels.MaxPercent.ShouldBe(PricingInputs.MaxVol * 100.0);
        IvUnits.FromVendor(IvLabels.MaxPercent).IsAvailable.ShouldBeTrue();
        IvUnits.FromVendor(IvLabels.MaxPercent + 0.01).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void Fifteen_and_seventeen_percent_give_sixteen_hundredths()
    {
        DailyAtmIv.Mean(15.0, 17.0)!.Value.ShouldBe(0.16, 1e-12);
    }

    [Theory]
    [InlineData(null, 17.0)]
    [InlineData(15.0, null)]
    [InlineData(0.0, 17.0)]
    [InlineData(15.0, -1.0)]
    [InlineData(15.0, double.NaN)]
    [InlineData(15.0, 600.0)]
    public void A_missing_or_invalid_side_gives_no_value_and_never_a_half_value_or_zero(double? ce, double? pe)
    {
        DailyAtmIv.Mean(ce, pe).ShouldBeNull();
    }

    [Fact]
    public void A_session_with_only_one_side_has_no_value_and_the_other_sessions_still_do()
    {
        var rows = new[]
        {
            new DailySideIv(D1, 1, "CE", 15.0, true), new DailySideIv(D1, 1, "PE", 17.0, true),
            new DailySideIv(D2, 1, "CE", 15.0, true),
        };

        var series = DailyAtmIv.Build(rows, _ => 1);

        series.Keys.ShouldBe([D1]);
        series[D1].ShouldBe(0.16, 1e-12);
    }

    [Fact]
    public void An_unusable_key_session_on_either_side_leaves_the_session_out()
    {
        var rows = new[] { new DailySideIv(D1, 1, "CE", 15.0, true), new DailySideIv(D1, 1, "PE", 17.0, false) };

        DailyAtmIv.Build(rows, _ => 1).ShouldBeEmpty();
    }

    [Fact]
    public void The_row_of_the_front_series_of_that_date_is_the_one_used()
    {
        var rows = new[]
        {
            new DailySideIv(D1, 1, "CE", 10.0, true), new DailySideIv(D1, 1, "PE", 10.0, true),
            new DailySideIv(D1, 2, "CE", 20.0, true), new DailySideIv(D1, 2, "PE", 20.0, true),
            new DailySideIv(D2, 1, "CE", 10.0, true), new DailySideIv(D2, 1, "PE", 10.0, true),
            new DailySideIv(D2, 2, "CE", 20.0, true), new DailySideIv(D2, 2, "PE", 20.0, true),
        };

        var series = DailyAtmIv.Build(rows, date => date == D1 ? 1 : date == D2 ? 2 : null);

        series[D1].ShouldBe(0.10, 1e-12);
        series[D2].ShouldBe(0.20, 1e-12);
    }

    [Fact]
    public void A_date_with_no_front_series_has_no_value()
    {
        var rows = new[] { new DailySideIv(D1, 1, "CE", 10.0, true), new DailySideIv(D1, 1, "PE", 10.0, true) };

        DailyAtmIv.Build(rows, _ => null).ShouldBeEmpty();
    }
}
