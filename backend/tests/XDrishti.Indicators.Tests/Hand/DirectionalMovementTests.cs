using static XDrishti.Indicators.Tests.Support.HandRun;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for plus_di, minus_di and adx (Wilder): the seed is the sum of the first n movements, the ADX is seeded with the mean of the first n DX values.</summary>
public sealed class DirectionalMovementTests
{
    /// <summary>
    /// n = 2. Per bar (+DM, −DM, TR): bar 2 (2, 0, 3); bar 3 (0, 1, 3); bar 4 (2, 0, 4); bar 5 (1, 0, 3); bar 6 (0, 2, 4). Bar 1 has no previous bar.
    /// </summary>
    private static readonly List<XDrishti.Domain.Market.Bars.Bar> Bars = Candles(
        (9, 10, 8, 9),
        (9, 12, 9, 11),
        (11, 11, 8, 9),
        (9, 13, 10, 12),
        (12, 14, 11, 13),
        (13, 12, 9, 10));

    [Fact]
    public void The_directional_indicators_are_seeded_with_the_sum_of_the_first_two_movements_and_ready_at_bar_three()
    {
        var plus = Run(new PlusDi(2), Bars);
        var minus = Run(new MinusDi(2), Bars);

        plus.Take(2).ShouldAllBe(v => double.IsNaN(v));
        minus.Take(2).ShouldAllBe(v => double.IsNaN(v));
        // bar 3: sums +DM 2, -DM 1, TR 6
        plus[2].ShouldBe(100.0 * 2 / 6, 1e-12);
        minus[2].ShouldBe(100.0 * 1 / 6, 1e-12);
        // bar 4: s = s - s/2 + x -> TR 6 - 3 + 4 = 7, +DM 2 - 1 + 2 = 3, -DM 1 - 0.5 + 0 = 0.5
        plus[3].ShouldBe(300.0 / 7, 1e-12);
        minus[3].ShouldBe(50.0 / 7, 1e-12);
        // bar 5: TR 7 - 3.5 + 3 = 6.5, +DM 3 - 1.5 + 1 = 2.5, -DM 0.5 - 0.25 + 0 = 0.25
        plus[4].ShouldBe(250.0 / 6.5, 1e-12);
        minus[4].ShouldBe(25.0 / 6.5, 1e-12);
        // bar 6: TR 6.5 - 3.25 + 4 = 7.25, +DM 2.5 - 1.25 + 0 = 1.25, -DM 0.25 - 0.125 + 2 = 2.125
        plus[5].ShouldBe(125.0 / 7.25, 1e-12);
        minus[5].ShouldBe(212.5 / 7.25, 1e-12);
    }

    [Fact]
    public void The_adx_is_seeded_with_the_mean_of_the_first_two_dx_values_at_bar_four_and_then_smoothed()
    {
        var adx = Run(new Adx(2), Bars);

        adx.Take(3).ShouldAllBe(v => double.IsNaN(v));
        // DX(3) = 100 * |33.33 - 16.67| / 50 = 100/3; DX(4) = 100 * |42.86 - 7.14| / 50 = 500/7; seed = their mean
        adx[3].ShouldBe((100.0 / 3 + 500.0 / 7) / 2, 1e-12);
        adx[3].ShouldBe(1100.0 / 21, 1e-12);
        // DX(5) = 900/11, adx = (adx * 1 + dx) / 2
        adx[4].ShouldBe(15500.0 / 231, 1e-12);
        // DX(6) = 700/27
        adx[5].ShouldBe((15500.0 / 231 + 700.0 / 27) / 2, 1e-12);
    }

    [Fact]
    public void An_outside_bar_where_the_upward_and_downward_moves_are_equal_moves_neither_way()
    {
        // previous high 10 low 8; next high 12 low 6: up = 2, down = 2 -> neither is larger, both movements are 0
        var bars = Candles((9, 10, 8, 9), (9, 12, 6, 9), (9, 12, 6, 9));

        Run(new PlusDi(2), bars)[2].ShouldBe(0.0);
        Run(new MinusDi(2), bars)[2].ShouldBe(0.0);
    }

    [Fact]
    public void A_series_that_never_moves_has_zero_directional_indicators_and_a_zero_adx()
    {
        var flat = Candles((5, 5, 5, 5), (5, 5, 5, 5), (5, 5, 5, 5), (5, 5, 5, 5), (5, 5, 5, 5));

        Run(new PlusDi(2), flat)[2].ShouldBe(0.0);
        Run(new MinusDi(2), flat)[4].ShouldBe(0.0);
        Run(new Adx(2), flat)[3].ShouldBe(0.0);
    }

    [Fact]
    public void A_period_of_one_makes_all_three_ready_at_bar_two()
    {
        var bars = Candles((9, 10, 8, 9), (9, 12, 9, 11));

        // +DM 2, TR 3 -> +DI 66.67, -DI 0, DX 100, adx = DX
        Run(new PlusDi(1), bars)[1].ShouldBe(200.0 / 3, 1e-12);
        Run(new MinusDi(1), bars)[1].ShouldBe(0.0);
        Run(new Adx(1), bars)[1].ShouldBe(100.0, 1e-12);
        new Adx(1).WarmUp.ShouldBe(2);
        new PlusDi(1).WarmUp.ShouldBe(2);
    }

    [Fact]
    public void Warm_up_is_n_plus_one_for_the_directional_indicators_and_two_n_for_the_adx()
    {
        new PlusDi(14).WarmUp.ShouldBe(15);
        new MinusDi(14).WarmUp.ShouldBe(15);
        new Adx(14).WarmUp.ShouldBe(28);
        Should.Throw<ArgumentOutOfRangeException>(() => new Adx(0));
    }
}
