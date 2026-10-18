using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;
using static XDrishti.Domain.Research.ExitReason;
using static XDrishti.Domain.Research.NoTradeReason;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The fixture days F1 to F17b and the rows W1 to W14 of the walker table (D9 of the spec), every number worked out by hand. The days only supply minutes: the candidates are made by hand, like the scanner of P3-03a
/// will make them. Indexes are positions in the bar list.
/// </summary>
internal static class Cases
{
    private static readonly WalkRules Rules = Cand.Standard;

    /// <summary>The days of the criteria 2 and 5: F1 to F7, F9, F12 to F17 and F17b.</summary>
    public static IReadOnlyList<WalkCase> FDays { get; } = BuildFDays();

    /// <summary>The rows W1 to W11, W13 and W14 that are a day and a candidate (W12 is a list of failures and is in the rule tests).</summary>
    public static IReadOnlyList<WalkCase> Rows { get; } = BuildRows();

    public static IReadOnlyList<WalkCase> All { get; } = [.. FDays, .. Rows];

    public static WalkCase F(string name) => FDays.Single(c => c.Name == name);

    public static WalkCase W(string name) => Rows.Single(c => c.Name == name);

    /// <summary>F1: B; the target is touched at 11:10.</summary>
    public static DayBuilder F1Day() => DayBuilder.Prefix().B()
        .Row("10:05 100.50/100.50/99.50/100.50")
        .Row("10:30 100.50/104.50/100.50/104.00")
        .Row("11:10 104.00/105.50/103.50/105.00");

    /// <summary>F5: B; the square-off at 15:15.</summary>
    public static DayBuilder F5Day() => DayBuilder.Prefix().B()
        .Row("10:00 100.50/102.00/100.50/102.00")
        .Row("11:40 102.00/102.00/99.00/101.50")
        .Row("13:00 101.50/104.50/101.50/103.00")
        .Row("15:15 103.00/106.00/97.00/103.00");

    private static ExpectedTrade Trade(string entryTime, double entry, double stop, double? target, string exitTime, double exit, ExitReason reason, double r, double mae, double mfe, int minutes) =>
        new(entryTime, entry, stop, target, exitTime, exit, reason, r, mae, mfe, minutes);

    private static WalkCase Case(string name, DayBuilder day, params WalkItem[] items) => new(name, day.Build(), Rules, items);

    private static WalkItem BuyB(ExpectedTrade trade) => WalkItem.Trades(Cand.B(), trade);

    private static List<WalkCase> BuildFDays()
    {
        var f1 = Trade("09:45", 100.50, 98.00, 105.50, "11:10", 105.50, Target, 2.0, 0.4, 2.0, 86);
        var sellF12 = Cand.Sell(29, "09:45", 100.00, "09:30", (99.00, 99.00, 97.50, 97.50));
        var f13Sell = Cand.Sell(59, "10:15", 100.00, "10:00", (100.50, 100.50, 97.50, 97.50));
        var f15 = Cand.Make(Side.Buy, 29, "09:46", 98.00, "09:30", (99.00, 100.50, 99.00, 100.50), minutes: 14, complete: false, volume: 14_000);
        var f16 = Cand.Make(Side.Buy, 14, "09:45", 98.00, "09:30", (99.00, 100.50, 99.00, 100.50), minutes: 1, complete: false, volume: 1_000);

        return
        [
            Case("F1", F1Day(), BuyB(f1)),
            Case("F2", DayBuilder.Prefix().B().Row("10:20 100.50/101.50/100.50/101.00").Row("10:50 101.00/102.00/97.50/98.50"),
                BuyB(Trade("09:45", 100.50, 98.00, 105.50, "10:50", 98.00, Stop, -1.0, 1.0, 0.4, 66))),
            Case("F3", DayBuilder.Prefix().B().Row("10:20 100.50/106.00/97.50/101.00"),
                BuyB(Trade("09:45", 100.50, 98.00, 105.50, "10:20", 98.00, Stop, -1.0, 1.0, 0.0, 36))),
            Case("F4", DayBuilder.Prefix().B().Row("09:45 100.50/100.50/97.50/98.50"),
                BuyB(Trade("09:45", 100.50, 98.00, 105.50, "09:45", 98.00, Stop, -1.0, 1.0, 0.0, 1))),
            Case("F5", F5Day(), BuyB(Trade("09:45", 100.50, 98.00, 105.50, "15:15", 103.00, SquareOff, 1.0, 0.6, 1.6, 331))),
            Case("F6", F5Day().Until("15:09"), BuyB(Trade("09:45", 100.50, 98.00, 105.50, "15:09", 103.00, EndOfData, 1.0, 0.6, 1.6, 325))),
            Case("F7", F5Day().Absent("15:10", "15:15").Row("15:16 103.50/106.00/97.00/103.50"),
                BuyB(Trade("09:45", 100.50, 98.00, 105.50, "15:16", 103.50, SquareOff, 1.2, 0.6, 1.6, 326))),
            Case("F9", DayBuilder.Prefix().Row("11:29 99.00/100.50/99.00/100.50"),
                WalkItem.Trades(
                    Cand.Make(Side.Buy, 134, "11:30", 98.00, "11:15", (99.00, 100.50, 99.00, 100.50)),
                    Trade("11:30", 100.50, 98.00, 105.50, "15:15", 100.50, SquareOff, 0.0, 0.0, 0.0, 226))),
            Case("F12", DayBuilder.Prefix().Row("09:44 99.00/99.00/97.50/97.50").Row("10:10 97.50/98.50/97.50/97.50").Row("11:30 97.50/97.50/95.00/96.00").Row("12:00 96.00/96.00/92.50/93.00"),
                WalkItem.Trades(sellF12, Trade("09:45", 97.50, 100.00, 92.50, "12:00", 92.50, Target, 2.0, 0.4, 2.0, 136))),
            Case("F13", DayBuilder.Prefix().B().Row("10:05 100.50/100.50/97.50/97.50"),
                BuyB(Trade("09:45", 100.50, 98.00, 105.50, "10:05", 98.00, Stop, -1.0, 1.0, 0.0, 21)),
                WalkItem.Trades(f13Sell, Trade("10:15", 97.50, 100.00, 92.50, "15:15", 97.50, SquareOff, 0.0, 0.0, 0.0, 301))),
            Case("F14", DayBuilder.Prefix().B().Row("09:45 97.75/98.50/97.75/98.00"), WalkItem.None(Cand.B(), EntryThroughStop, 1)),
            Case("F15", DayBuilder.Prefix().Row("09:43 99.00/100.50/99.00/100.50").Absent("09:44", "09:44"),
                WalkItem.Trades(f15, Trade("09:46", 100.50, 98.00, 105.50, "15:15", 100.50, SquareOff, 0.0, 0.0, 0.0, 330))),
            Case("F16", DayBuilder.Prefix().Absent("09:29", "09:43").Row("09:44 99.00/100.50/99.00/100.50"),
                WalkItem.Trades(f16, Trade("09:45", 100.50, 98.00, 105.50, "15:15", 100.50, SquareOff, 0.0, 0.0, 0.0, 331))),
            Case("F17", F1Day().Absent("09:45", "09:45"),
                BuyB(new ExpectedTrade("09:46", 100.50, 98.00, 105.50, "11:10", 105.50, Target, 2.0, 0.4, 2.0, 85, Delayed: true))),
            Case("F17b-first", F1Day().Absent("09:45", "09:49"),
                BuyB(new ExpectedTrade("09:50", 100.50, 98.00, 105.50, "11:10", 105.50, Target, 2.0, 0.4, 2.0, 81, Delayed: true))),
            Case("F17b-second", F1Day().Absent("09:45", "09:50"), WalkItem.None(Cand.B(), NoFillBar, 0)),
        ];
    }

    private static List<WalkCase> BuildRows()
    {
        var sell = Cand.Sell(29, "09:45", 100.00, "09:30", (99.00, 99.00, 97.50, 97.50));
        var noTarget = Rules with { RewardRisk = null };
        var tuesday = DayBuilder.Monday.AddDays(1);
        var nextDay = DayBuilder.Prefix(tuesday).Row("09:15 50.00/1000.00/0.01/50.00").Row("09:16 0.01/0.01/0.01/0.01").Until("09:20").Build();

        return
        [
            Case("W1", DayBuilder.Prefix().B().Row("09:45 97.75"), WalkItem.None(Cand.B(), EntryThroughStop, 1)),
            Case("W2", DayBuilder.Prefix().B().Row("09:45 98.00"), WalkItem.None(Cand.B(), EntryThroughStop, 1)),
            Case("W3", DayBuilder.Prefix().B().Row("09:45 98.25/98.75/98.25/98.50"), BuyB(Trade("09:45", 98.25, 98.00, 98.75, "09:45", 98.75, Target, 2.0, 0.0, 2.0, 1))),
            Case("W4-opens-at-the-stop", DayBuilder.Prefix().Row("09:45 100.00"), WalkItem.None(sell, EntryThroughStop, 1)),
            Case("W4-one-tick-inside", DayBuilder.Prefix().Row("09:45 99.75"), WalkItem.Trades(sell, Trade("09:45", 99.75, 100.00, 99.25, "15:15", 99.75, SquareOff, 0.0, 0.0, 0.0, 331))),
            Case("W5", DayBuilder.Prefix().B().Row("10:00 100.00/100.00/98.00/99.00"), BuyB(Trade("09:45", 100.50, 98.00, 105.50, "10:00", 98.00, Stop, -1.0, 1.0, 0.0, 16))),
            Case("W6", DayBuilder.Prefix().B().Row("10:30 97.00/97.75/96.50/97.25"),
                BuyB(new ExpectedTrade("09:45", 100.50, 98.00, 105.50, "10:30", 97.00, Stop, -1.4, 1.4, 0.0, 46, StopGap: true))),
            Case("W7", DayBuilder.Prefix().B().Row("10:30 106.00/106.50/105.75/106.25"),
                BuyB(new ExpectedTrade("09:45", 100.50, 98.00, 105.50, "10:30", 105.50, Target, 2.0, 0.0, 2.0, 46, TargetGap: true))),
            new WalkCase("W8-to-the-square-off", DayBuilder.Prefix().B().Build(), noTarget, [BuyB(Trade("09:45", 100.50, 98.00, null, "15:15", 100.50, SquareOff, 0.0, 0.0, 0.0, 331))]),
            new WalkCase("W8-to-the-stop", DayBuilder.Prefix().B().Row("10:30 100.50/101.00/97.50/98.00").Build(), noTarget, [BuyB(Trade("09:45", 100.50, 98.00, null, "10:30", 98.00, Stop, -1.0, 1.0, 0.0, 46))]),
            Case("W9-last-minute-of-the-list", DayBuilder.Prefix().B().Until("09:44"), WalkItem.None(Cand.B(), NoFillBar, 0)),
            new WalkCase(
                "W9-next-minute-on-the-next-date",
                [.. DayBuilder.Prefix().Build(), .. nextDay],
                Rules,
                [WalkItem.None(Cand.Make(Side.Buy, 374, "15:30", 98.00, "15:15", (99.00, 99.00, 99.00, 99.00)), NoFillBar, 0)]),
            Case("W9b-five-minutes-after", F1Day().Absent("09:45", "09:49"), BuyB(new ExpectedTrade("09:50", 100.50, 98.00, 105.50, "11:10", 105.50, Target, 2.0, 0.4, 2.0, 81, Delayed: true))),
            Case("W9b-six-minutes-after", F1Day().Absent("09:45", "09:50"), WalkItem.None(Cand.B(), NoFillBar, 0)),
            Case("W10", DayBuilder.Prefix(), WalkItem.None(Cand.Make(Side.Buy, 359, "15:15", 98.00, "15:00", (99.00, 99.00, 99.00, 99.00)), AfterSquareOff, 0)),
            new WalkCase(
                "W11",
                [.. F5Day().Until("15:09").Build(), .. nextDay],
                Rules,
                [BuyB(Trade("09:45", 100.50, 98.00, 105.50, "15:09", 103.00, EndOfData, 1.0, 0.6, 1.6, 325))]),
            Case("W13", DayBuilder.Prefix().B().Row("10:30 106.00/106.50/97.50/100.00"), BuyB(Trade("09:45", 100.50, 98.00, 105.50, "10:30", 98.00, Stop, -1.0, 1.0, 0.0, 46))),
            Case("W14-opens-beyond-the-stop", DayBuilder.Prefix().B().Row("15:15 97.50/97.75/97.00/97.25"), BuyB(Trade("09:45", 100.50, 98.00, 105.50, "15:15", 97.50, SquareOff, -1.2, 1.2, 0.0, 331))),
            Case("W14-opens-beyond-the-target", DayBuilder.Prefix().B().Row("15:15 106.00/106.50/105.75/106.25"), BuyB(Trade("09:45", 100.50, 98.00, 105.50, "15:15", 106.00, SquareOff, 2.2, 0.0, 2.2, 331))),
        ];
    }
}
