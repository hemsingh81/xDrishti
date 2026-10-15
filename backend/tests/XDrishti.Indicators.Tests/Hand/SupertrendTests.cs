using XDrishti.Indicators.Tests.Support;
using static XDrishti.Indicators.Tests.Support.HandRun;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>
/// Hand-calculated Supertrend (n = 1 so the ATR is the true range): the final bands carry over and reset when the previous close was beyond them; then the direction flips on the close beyond the <i>current</i>
/// final band (TradingView and Skender), in each direction.
/// </summary>
public sealed class SupertrendTests
{
    // bar:      high low close  true range  hl2    basic upper / lower
    private static readonly List<XDrishti.Domain.Market.Bars.Bar> Bars = Candles(
        (10, 11, 9, 10),        // 1: tr 2,   hl2 10,   12 / 8
        (10, 12, 10, 11),       // 2: tr 2,   hl2 11,   13 / 9
        (11, 13, 11, 12.5),     // 3: tr 2,   hl2 12,   14 / 10
        (12.5, 14, 12, 13),     // 4: tr 2,   hl2 13,   15 / 11
        (13, 12, 8, 9),         // 5: tr 5,   hl2 10,   15 / 5
        (9, 10, 8, 9),          // 6: tr 2,   hl2 9,    11 / 7
        (9, 11, 9, 10),         // 7: tr 2,   hl2 10,   12 / 8
        (10, 14, 11, 13));      // 8: tr 4,   hl2 12.5, 16.5 / 8.5

    [Fact]
    public void The_line_follows_the_final_bands_with_a_flip_down_and_a_flip_up()
    {
        var line = Run(new Supertrend(1, 1), Bars);
        var direction = Run(new SupertrendDirection(1, 1), Bars);

        // 1: starts up on the basic bands 12 / 8 -> line = lower 8
        // 2: upper 13 is not below 12 and the previous close 10 is not above 12 -> stays 12; lower 9 > 8 -> 9; line 9
        // 3: upper stays 12; lower 10; line 10
        // 4: the previous close 12.5 is above the carried upper 12 -> upper resets to 15; lower 11; line 11
        // 5: upper 15 carried (basic 15 is not below it); lower 11 carried (basic 5, previous close 13 is not below 11); close 9 is below the current final lower 11 -> flips down, line = upper 15
        // 6: basic upper 11 < 15 -> 11; previous close 9 < lower 11 -> lower resets to 7; line = upper 11
        // 7: basic upper 12 is not below 11, previous close 9 is not above 11 -> stays 11 (carry-over); line 11
        // 8: upper stays 11 (basic 16.5, previous close 10 is not above 11); lower 8.5 > 8 -> 8.5; close 13 is above the current final upper 11 -> flips up, line = lower 8.5
        line.ShouldBe([8, 9, 10, 11, 15, 11, 11, 8.5]);
        direction.ShouldBe([1, 1, 1, 1, -1, -1, -1, 1]);
    }

    [Fact]
    public void A_close_beyond_the_current_final_band_flips_even_when_it_is_not_beyond_the_previous_one()
    {
        // n = 1, multiplier 0.25 (offset = a quarter of the true range): the bands move towards the close, so the two rules separate
        var bars = Candles(
            (10, 12, 8, 10),        // 1: tr 4, hl2 10,   basic 11 / 9
            (10, 14, 10, 13),       // 2: tr 4, hl2 12,   basic 13 / 11
            (13, 16, 12, 12.5),     // 3: tr 4, hl2 14,   basic 15 / 13
            (12.5, 14, 12, 13),     // 4: tr 2, hl2 13,   basic 13.5 / 12.5
            (13, 13.5, 12.5, 13.5), // 5: tr 1, hl2 13,   basic 13.25 / 12.75
            (13.5, 14.5, 13.5, 14)); // 6: tr 1, hl2 14,  basic 14.25 / 13.75

        var line = Run(new Supertrend(1, 0.25), bars);
        var direction = Run(new SupertrendDirection(1, 0.25), bars);

        // 1: starts up on 11 / 9 -> line = lower 9
        // 2: upper 13 is not below 11 and the previous close 10 is not above 11 -> carried 11; lower 11 > 9 -> 11; close 13 is not below 11; line 11
        // 3: the previous close 13 is above the carried upper 11 -> upper 15; lower 13 > 11 -> 13; close 12.5 is below the CURRENT final lower 13 -> flips down
        //    (it is above the previous final lower 11, so a previous-band rule would stay up); line = upper 15
        // 4: upper 13.5 < 15 -> 13.5; basic lower 12.5 is not above 13 but the previous close 12.5 is below 13 -> lower 12.5; close 13 is not above 13.5; line 13.5
        // 5: upper 13.25 < 13.5 -> 13.25; lower 12.75 > 12.5 -> 12.75; close 13.5 is above the CURRENT final upper 13.25 (not above the previous 13.5, so a previous-band rule would stay down) -> flips up; line = lower 12.75
        // 6: the previous close 13.5 is above the carried upper 13.25 -> upper 14.25; lower 13.75 > 12.75 -> 13.75; close 14 is not below 13.75; line 13.75
        line.ShouldBe([9, 11, 15, 13.5, 12.75, 13.75]);
        direction.ShouldBe([1, 1, -1, -1, 1, 1]);
    }

    [Fact]
    public void It_is_ready_at_bar_n_with_the_direction_up()
    {
        var bars = Candles((10, 11, 9, 10), (10, 12, 10, 11), (11, 13, 11, 12.5));
        var supertrend = new Supertrend(2, 1);
        var direction = new SupertrendDirection(2, 1);

        var line = Run(supertrend, bars);
        var dir = Run(direction, bars);

        line[0].ShouldBeNaN();
        dir[0].ShouldBeNaN();
        supertrend.WarmUp.ShouldBe(2);
        // tr 2, 2 -> atr(2) = 2 at bar 2; hl2 11, basic lower 11 - 2 = 9: first value is the lower band, direction up
        line[1].ShouldBe(9.0, 1e-12);
        dir[1].ShouldBe(1.0);
    }

    [Fact]
    public void The_multiplier_must_be_above_zero_and_finite()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Supertrend(10, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => new SupertrendDirection(10, -3));
        Should.Throw<ArgumentOutOfRangeException>(() => new Supertrend(10, double.NaN));
        Should.Throw<ArgumentOutOfRangeException>(() => new Supertrend(0, 3));
    }
}
