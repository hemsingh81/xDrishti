using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>One invented stream of the fixtures: a name, the strategy it is scanned with, the evaluation timeframe and the bars.</summary>
internal sealed record Fixture(string Name, StrategyDefinition Definition, Timeframe Timeframe, Bar[] Bars);

/// <summary>The invented days of P3-03 D9 and P3-03a D12, written as the specs write them. Date 2026-03-02 unless stated; the scanner column of each is in the tests.</summary>
internal static class Fixtures
{
    public static Day F1() => Day.P().B().Set("10:05 100.50/100.50/99.50/100.50", "10:30 100.50/104.50/100.50/104.00", "11:10 104.00/105.50/103.50/105.00");

    public static Day F2() => Day.P().B().Set("10:20 100.50/101.50/100.50/101.00", "10:50 101.00/102.00/97.50/98.50");

    public static Day F3() => Day.P().B().Set("10:20 100.50/106.00/97.50/101.00");

    public static Day F4() => Day.P().B().Set("09:45 100.50/100.50/97.50/98.50");

    public static Day F5() => Day.P().B().Set("10:00 100.50/102.00/100.50/102.00", "11:40 102.00/102.00/99.00/101.50", "13:00 101.50/104.50/101.50/103.00", "15:15 103.00/106.00/97.00/103.00");

    public static Day F6() => F5().EndsAfter("15:09");

    public static Day F7() => F5().Absent("15:10", "15:15").Set("15:16 103.50/106.00/97.00/103.50");

    public static Day F8() => Day.P().Set("09:44 99.00/100.00/99.00/100.00");

    public static Day F9() => Day.P().Set("11:29 99.00/100.50/99.00/100.50");

    public static Day F10() => Day.P().Set("11:44 99.00/100.50/99.00/100.50");

    public static Day F11() => F2().Set("11:14 98.50/100.50/98.50/100.50");

    public static Day F12() => Day.P().Set("09:44 99.00/99.00/97.50/97.50", "10:10 97.50/98.50/97.50/97.50", "11:30 97.50/97.50/95.00/96.00", "12:00 96.00/96.00/92.50/93.00");

    public static Day F13() => Day.P().B().Set("10:05 100.50/100.50/97.50/97.50");

    public static Day F14() => Day.P().B().Set("09:45 97.75/98.50/97.75/98.00");

    public static Day F15() => Day.P().Set("09:43 99.00/100.50/99.00/100.50").Absent("09:44");

    public static Day F16() => Day.P().Absent("09:29", "09:43").Set("09:44 99.00/100.50/99.00/100.50");

    public static Day F17() => F1().Absent("09:45");

    public static Day F17bFirst() => F1().Absent("09:45", "09:49");

    public static Day F17bSecond() => F1().Absent("09:45", "09:50");

    /// <summary>F18: day A without its last minute (15:29) and day B with its first 15 minutes only, all flat 99.00.</summary>
    public static Bar[] F18() => Day.Join(Day.Dense(last: "15:28").Start(99.00), Day.Dense(new DateOnly(2026, 3, 4), last: "09:29").Start(99.00));

    /// <summary>F19: the 09:35 minute dips under the low of the opening range at the bar that breaks out.</summary>
    public static Day F19() => Day.P().B().Set("09:35 99.00/99.00/97.50/99.00");

    /// <summary>F21: three short days, A (nothing), B (the close is already above the VWAP at the first bar) and C (a real crossing at the second bar).</summary>
    public static Bar[] F21() => Day.Join(
        Day.Dense(Day.Monday, last: "09:29").Start(99.00),
        Day.Dense(new DateOnly(2026, 3, 4), last: "09:44").Start(99.00).Set("09:29 99.00/101.00/99.00/101.00"),
        Day.Dense(new DateOnly(2026, 3, 5), last: "09:45").Start(99.00).Set("09:44 99.00/101.00/99.00/101.00", "09:45 101.00/101.00/101.00/101.00"));

    /// <summary>The scanner fixtures that run with ORB v1 on 15m, by name.</summary>
    public static IReadOnlyList<Fixture> OrbDays { get; } = BuildOrbDays();

    /// <summary>All of F1 to F23 with the strategy each is scanned with (the order of the golden file).</summary>
    public static IReadOnlyList<Fixture> All { get; } = BuildAll();

    private static List<Fixture> BuildOrbDays()
    {
        var orb = XDrishti.Strategies.BuiltIn.OrbV1.Create();
        Fixture Orb(string name, Day day) => new(name, orb, Timeframe.M15, day.Bars());
        return
        [
            Orb("F1", F1()), Orb("F2", F2()), Orb("F3", F3()), Orb("F4", F4()), Orb("F5", F5()), Orb("F6", F6()), Orb("F7", F7()), Orb("F8", F8()), Orb("F9", F9()), Orb("F10", F10()),
            Orb("F11", F11()), Orb("F12", F12()), Orb("F13", F13()), Orb("F14", F14()), Orb("F15", F15()), Orb("F16", F16()), Orb("F17", F17()), Orb("F17b-first", F17bFirst()), Orb("F17b-second", F17bSecond()),
            new Fixture("F18", orb, Timeframe.M15, F18()),
        ];
    }

    private static List<Fixture> BuildAll()
    {
        var all = BuildOrbDays();
        all.Add(new Fixture("F18b", TestStrategies.StaleTest(), Timeframe.M15, F18()));
        all.Add(new Fixture("F19", TestStrategies.InvalidateOnLowUnderRange(), Timeframe.M15, F19().Bars()));
        all.Add(new Fixture("F19b", TestStrategies.InvalidateOnLowUnderRange(), Timeframe.M15, F11().Bars()));
        all.Add(new Fixture("F20", TestStrategies.SetupAfterTen(), Timeframe.M15, Day.P().B().Bars()));
        all.Add(new Fixture("F21", TestStrategies.CrossVwap(), Timeframe.M15, F21()));
        all.Add(new Fixture("F22", TestStrategies.StopNotReady(), Timeframe.M15, Day.P().B().Bars()));
        all.Add(new Fixture("F23", TestStrategies.InvalidateUnknownEarly(), Timeframe.M15, Day.P().B().Bars()));
        return all;
    }
}
