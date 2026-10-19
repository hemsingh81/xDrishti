using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The hand-calculated days V1 to V7 of P3-05 (D9): a banded prefix of 21 sessions, then the fixture day on the Monday after it. Every list is the prefix followed by the day, one bar list for the replay.</summary>
internal static class VwapDays
{
    /// <summary>The fixture day: a Monday, so the 21 prefix sessions are the 21 weekdays before it.</summary>
    public static readonly DateOnly Date = Day.Monday;

    /// <summary>The bars of the day after V1's six bars to the target (the rows of the V1 line of D9).</summary>
    private static Day ToTarget(Day day) => day.Set("10:50 113.75/113.75/113.25/113.50", "11:10 113.50/115.00/113.50/115.00", "11:30 115.00/116.25/115.00/116.00");

    /// <summary>The rows of the V2 line of D9 (to the stop).</summary>
    private static Day ToStop(Day day) => day.Set("10:50 113.75/114.00/113.75/114.00", "11:05 114.00/114.00/112.50/112.50");

    private static Day Fresh() => Day.Dense(Date).Start(BandedDays.Pivot);

    public static Day V1Day() => ToTarget(BandedDays.V1Bars(Fresh()));

    public static Day V2Day() => ToStop(BandedDays.V1Bars(Fresh()));

    /// <summary>V4: fifteen flat bars at 110.50 (O 110.50 H 111.00 L 110.00 C 110.50), then V1's six bars at 13:00 to 14:15, flat 113.75 to the end.</summary>
    public static Day V4Day(int extraFlatBars = 0)
    {
        var day = Fresh();
        var flat = 15 + extraFlatBars;
        for (var i = 0; i < flat; i++)
        {
            day.Set(Day.Clock(9 * 60 + 15 + 15 * i) + " 110.50/111.00/110.00/110.50");
        }

        return BandedDays.V1Bars(day, Day.Clock(9 * 60 + 15 + 15 * flat));
    }

    public static Bar[] V1() => [.. BandedDays.Up(Date), .. V1Day().Bars()];

    public static Bar[] V2() => [.. BandedDays.Up(Date), .. V2Day().Bars()];

    /// <summary>V3: the down-trend prefix, V1's day.</summary>
    public static Bar[] V3() => [.. BandedDays.Down(Date), .. V1Day().Bars()];

    /// <summary>V4: the up-trend prefix and the pullback that is too shallow for 0.5 ATR.</summary>
    public static Bar[] V4(int extraFlatBars = 0) => [.. BandedDays.Up(Date), .. V4Day(extraFlatBars).Bars()];

    /// <summary>V5: the down-trend prefix and V1's day reflected about 110.50.</summary>
    public static Bar[] V5() => [.. BandedDays.Down(Date), .. BandedDays.Mirror(V1Day().Bars())];

    /// <summary>V6: only five prefix sessions, V1's day.</summary>
    public static Bar[] V6() => [.. BandedDays.Up(Date, 5), .. V1Day().Bars()];

    /// <summary>V7: V1 as the all-day variant of the definition runs it (same bars as V1).</summary>
    public static Bar[] V7() => V1();

    /// <summary>The test-only strategy of V7: the definition of D1 with no first and no last entry.</summary>
    public static StrategyDefinition AllDay() => VwapPullback.Create() with
    {
        Id = "VWAP_PULLBACK_ALLDAY",
        TimeRules = new TimeRules(null, null, new TimeOnly(15, 15), null),
    };
}
