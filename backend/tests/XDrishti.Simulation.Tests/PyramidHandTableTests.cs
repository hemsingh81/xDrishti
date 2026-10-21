using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 2 of P4-04: twenty-five <c>PYRAMID</c> rows computed by hand from D1 to D9, buys and sells, intraday and swing, with the ladder of the spec (<c>initial_pct</c> 50, <c>add_at_rr</c> 1 and 2, <c>add_pct</c> 30 and 20): no add, one
/// add, two adds, a close beyond two levels, <c>max_adds</c> 1 of 2, an add missed by the limit (a buy and a sell), an add of no whole share, the stop before the add, the stop and the add in one minute, the new stop touched in the add's own minute,
/// a gap through the combined stop (a buy and a sell), the trail above the risk stop, the square-off, <c>MAX_HOLD</c>, the end of the data and the time stop with adds open, a swing add across the session break, and slippage. The frame is that
/// of <see cref="TrailRow"/>: entry 100.00, stop 99.00 (a sell 101.00), <c>R0</c> 1.00, the unit of R the risk of the full size. Row 1 and row 2 are the two hand rows of the spec. A row with an add skipped by the risk rule is not in the table:
/// the rule cannot skip (ADR 0052, decision 4), which <c>PyramidPropertyTests</c> holds over every seeded series.
/// </summary>
public sealed class PyramidHandTableTests
{
    private static TrailBar B(string at, double close, double atr = double.NaN) => new(at, close, atr);

    private static WantLeg Entry(double price = 100.00, double? filled = null) => new(LegKind.Entry, "09:45", price, 0.5m, filled);

    private static WantLeg Add(string at, double price, decimal share, double? filled = null) => new(LegKind.Add, at, price, share, filled);

    private static WantLeg Exit(string at, double price, decimal share) => new(LegKind.Exit, at, price, share);

    public static TheoryData<PyramidRow> Rows { get; } =
    [
        // P1 (spec hand row 1): bar 1 closes 101.20 (progress 1.2 >= 1), the next open 101.25 is within the limit 101.30; S_req = (0.5 x 100 + 0.3 x 101.25 - 1.0) / 0.8 = 79.375 / 0.8 = 99.21875, tighter than 99.00, in force from 10:00.
        // 10:20 trades down to 99.20: all 0.8 leave at 99.21875. R = 0.8 x 99.21875 - 80.375 = -1.00. Segment 1: avg 100, high 101.20 (MFE 1.20). Segment 2: avg 100.46875, the stop exit worsens to 99.21875 (MAE 1.25)
        new PyramidRow("P1 hand row 1 of the spec: the cap holds a stopped pyramid to exactly -1R", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:20 101.25/101.25/99.20/99.30"], [B("10:00", 101.20), B("10:15", 101.25)],
            new PyramidWant(ExitReason.Stop, "10:20", 99.21875, -1.00, 1.25, 1.20, [Entry(), Add("10:00", 101.25, 0.3m), Exit("10:20", 99.21875, 0.8m)], Taken: 1)),

        // P2 (spec hand row 2): add 2 (20 %) at 102.40 after a close at 102.30 (limit 102.40: a tie fills); S_req = (50 + 30.375 + 20.48 - 1.0) / 1.0 = 99.855, tighter than 99.21875; square-off at 103.00: R = 103.00 - 100.855 = +2.145.
        // Segment 3 starts at the average 100.855 and its high is 103.00: MFE 2.145; no adverse move: MAE 0
        new PyramidRow("P2 hand row 2 of the spec: two adds and the square-off", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:14 101.25/102.30/101.25/102.30", "10:15 102.40/102.40/102.40/102.40", "10:29 102.40/103.00/102.40/103.00"],
            [B("10:00", 101.20), B("10:15", 102.30), B("10:30", 103.00)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 103.00, 2.145, 0, 2.145, [Entry(), Add("10:00", 101.25, 0.3m), Add("10:15", 102.40, 0.2m), Exit("15:15", 103.00, 1.0m)], Taken: 2)),

        // P3: the mirror of P1 as a sell: close 98.80, limit Up(98.7012) = 98.75, the open 98.75 fills (a tie); S_req = (50 + 29.625 + 1.0) / 0.8 = 100.78125, tighter than 101.00; 10:20 trades up to 100.80: out at 100.78125. R -1.00, MAE 1.25, MFE 1.20
        new PyramidRow("P3 the mirror sell of P1", Side.Sell,
            ["09:59 100.00/100.00/98.80/98.80", "10:00 98.75/98.75/98.75/98.75", "10:20 98.75/100.80/98.75/100.70"], [B("10:00", 98.80), B("10:15", 98.75)],
            new PyramidWant(ExitReason.Stop, "10:20", 100.78125, -1.00, 1.25, 1.20, [Entry(), Add("10:00", 98.75, 0.3m), Exit("10:20", 100.78125, 0.8m)], Taken: 1)),

        // P4: max_adds 1 of 2: the close of 102.30 at 10:15 attempts nothing; the square-off at 103.00 takes 0.8: R = 0.8 x 103.00 - 80.375 = 2.025; MFE 103.00 - 100.46875 = 2.53125
        new PyramidRow("P4 max_adds 1 of 2 leaves the second level unused", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:14 101.25/102.30/101.25/102.30", "10:15 102.40/102.40/102.40/102.40", "10:29 102.40/103.00/102.40/103.00"],
            [B("10:00", 101.20), B("10:15", 102.30), B("10:30", 103.00)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 103.00, 2.025, 0, 2.53125, [Entry(), Add("10:00", 101.25, 0.3m), Exit("15:15", 103.00, 0.8m)], Taken: 1), MaxAdds: 1),

        // P5: the next open 101.35 is beyond the limit 101.30: the add is missed and its level is consumed; the half position leaves at the square-off 101.35: R = 0.5 x 1.35 = 0.675, MFE 1.35
        new PyramidRow("P5 an add missed by the limit", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.35/101.35/101.35/101.35"], [B("10:00", 101.20), B("10:15", 101.35)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 101.35, 0.675, 0, 1.35, [Entry(), Exit("15:15", 101.35, 0.5m)], Missed: 1)),

        // P6: a full size of 3 shares: 0.3 x 3 = 0.9 shares, floor 0: the add is skipped for its quantity; square-off at 101.25: R = 0.5 x 1.25 = 0.625, MFE 1.25
        new PyramidRow("P6 an add that holds no whole share", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25"], [B("10:00", 101.20), B("10:15", 101.25)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 101.25, 0.625, 0, 1.25, [Entry(), Exit("15:15", 101.25, 0.5m)], SkippedQuantity: 1), FullSize: 3),

        // P7: one close of 102.30 is beyond two levels: the first is attempted now (limit 102.40, the open 102.40 ties: add 0.3, S = 99.65), the second at the next bar that closes there (10:15, open 102.35: add 0.2, S_req = (50 + 30.72 + 20.47 - 1) / 1 = 100.19).
        // Square-off at 102.35: R = 102.35 - 101.19 = 1.16; MFE 2.30 (segment 1)
        new PyramidRow("P7 a close beyond two levels attempts one at a time", Side.Buy,
            ["09:59 100.00/102.30/100.00/102.30", "10:00 102.40/102.40/102.40/102.40", "10:15 102.35/102.35/102.35/102.35"], [B("10:00", 102.30), B("10:15", 102.30)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 102.35, 1.16, 0, 2.30, [Entry(), Add("10:00", 102.40, 0.3m), Add("10:15", 102.35, 0.2m), Exit("15:15", 102.35, 1.0m)], Taken: 2)),

        // P9: the stop is hit before any bar asks for an add: the half position loses 0.5 R (the unit is the full size): R = 0.5 x (99.00 - 100.00) = -0.5, MAE 1.00, MFE 0.40
        new PyramidRow("P9 the initial stop before any add loses the initial share of R", Side.Buy,
            ["09:59 100.00/100.40/100.00/100.40", "10:05 100.40/100.40/98.90/99.00"], [B("10:00", 100.40)],
            new PyramidWant(ExitReason.Stop, "10:05", 99.00, -0.5, 1.00, 0.40, [Entry(), Exit("10:05", 99.00, 0.5m)])),

        // P10: the add and the stop in one minute: the minute reaches the stop in force before the add (low 98.90 <= 99.00), so it is a stop exit and the add is not made (and not counted): R = -0.5
        new PyramidRow("P10 a minute that reaches the old stop is a stop exit and the add is not made", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/98.90/99.00"], [B("10:00", 101.20)],
            new PyramidWant(ExitReason.Stop, "10:00", 99.00, -0.5, 1.00, 1.20, [Entry(), Exit("10:00", 99.00, 0.5m)])),

        // P11: the add is made at 10:00 (low 99.10 does not reach the old stop 99.00) and the new stop 99.21875 is touched in the same minute: all 0.8 leave there. R = -1.00, MAE 1.25 (segment 2 worsens to the stop), MFE 1.20
        new PyramidRow("P11 the new stop touched in the add's own minute", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/99.10/99.15"], [B("10:00", 101.20)],
            new PyramidWant(ExitReason.Stop, "10:00", 99.21875, -1.00, 1.25, 1.20, [Entry(), Add("10:00", 101.25, 0.3m), Exit("10:00", 99.21875, 0.8m)], Taken: 1)),

        // P12: a gap through the combined stop with the add open: 10:30 opens 98.90 below 99.21875: out at the open. R = 0.8 x 98.90 - 80.375 = -1.255, MAE 100.46875 - 98.90 = 1.56875
        new PyramidRow("P12 a gap through the combined stop leaves at the open", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:30 98.90/99.00/98.80/98.90"], [B("10:00", 101.20), B("10:15", 101.25)],
            new PyramidWant(ExitReason.Stop, "10:30", 98.90, -1.255, 1.56875, 1.20, [Entry(), Add("10:00", 101.25, 0.3m), Exit("10:30", 98.90, 0.8m)], Taken: 1, StopGap: true)),

        // P13: bar 2 closes 103.00 with ATR 0.5, mult 2: H = 103.20 (the high of 10:14), level 102.20 from 10:15, tighter than 99.21875; the same bar asks for add 2 (open 103.00, S_req = 99.975, looser than 102.20: the stop stays 102.20).
        // 10:30 trades down to 102.10: out at 102.20. R = 102.20 - 100.975 = 1.225; MFE 103.20 - 100.46875 = 2.73125
        new PyramidRow("P13 the trail above the risk stop is the stop", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:14 101.25/103.20/101.25/103.00", "10:15 103.00/103.00/103.00/103.00", "10:30 103.00/103.00/102.10/102.15"],
            [B("10:00", 101.20), B("10:15", 103.00, 0.5)],
            new PyramidWant(ExitReason.Stop, "10:30", 102.20, 1.225, 0, 2.73125, [Entry(), Add("10:00", 101.25, 0.3m), Add("10:15", 103.00, 0.2m), Exit("10:30", 102.20, 1.0m)], Taken: 2), Multiple: 2m),

        // P15: swing, hold 2: the add of Monday stays open; MAX_HOLD at the close of Tuesday's 15:29, 101.40: R = 0.8 x 101.40 - 80.375 = 0.745; MFE 1.20 (segment 1)
        new PyramidRow("P15 MAX_HOLD with an add open", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25"], [B("10:00", 101.20), B("10:15", 101.25)],
            new PyramidWant(ExitReason.MaxHold, "15:29", 101.40, 0.745, 0, 1.20, [Entry(), Add("10:00", 101.25, 0.3m), new WantLeg(LegKind.Exit, "+1 15:29", 101.40, 0.8m)], Taken: 1, Sessions: 2, Day: 1),
            ["13:00 101.25/101.50/101.25/101.40"], Hold: 2),

        // P16: E = 1D: the bar of Monday closes at 15:30 at 101.20; Tuesday's first open 101.25 is within the limit: the add fills at the next session's open. MAX_HOLD at Tuesday's close 101.25: R = 0.8 x 101.25 - 80.375 = 0.625
        new PyramidRow("P16 a swing add across the session break", Side.Buy,
            ["10:00 100.00/101.20/100.00/101.20"], [B("15:30", 101.20)],
            new PyramidWant(ExitReason.MaxHold, "15:29", 101.25, 0.625, 0, 1.20, [Entry(), new WantLeg(LegKind.Add, "+1 09:15", 101.25, 0.3m), new WantLeg(LegKind.Exit, "+1 15:29", 101.25, 0.8m)], Taken: 1, Sessions: 2, Day: 1),
            ["09:15 101.25/101.25/101.25/101.25"], Hold: 2),

        // P17: the bar closes at 15:15, the square-off minute: nothing is attempted and the half position leaves at the square-off open 101.20: R = 0.5 x 1.20 = 0.60
        new PyramidRow("P17 a bar that closes at the square-off attempts no add", Side.Buy,
            ["15:14 100.00/101.20/100.00/101.20"], [B("15:15", 101.20)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 101.20, 0.60, 0, 1.20, [Entry(), Exit("15:15", 101.20, 0.5m)])),

        // P18: a bar that closes at 15:00 still asks for an add: it fills at the 15:00 open 101.25 and the square-off at 15:15 takes 0.8 at 101.25: R = 0.625
        new PyramidRow("P18 an add shortly before the square-off", Side.Buy,
            ["14:59 100.00/101.20/100.00/101.20", "15:00 101.25/101.25/101.25/101.25"], [B("15:00", 101.20)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 101.25, 0.625, 0, 1.20, [Entry(), Add("15:00", 101.25, 0.3m), Exit("15:15", 101.25, 0.8m)], Taken: 1)),

        // P20: the intraday time stop (2 bars, minimum progress 0.5) with the add open: bar 2 closes 100.30 (progress 0.3): out at the 10:15 open 100.30. R = 0.8 x 100.30 - 80.375 = -0.135; MAE 100.46875 - 100.30 = 0.16875
        new PyramidRow("P20 the time stop with an add open", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:14 101.25/101.25/100.30/100.30"], [B("10:00", 101.20), B("10:15", 100.30)],
            new PyramidWant(ExitReason.TimeStop, "10:15", 100.30, -0.135, 0.16875, 1.20, [Entry(), Add("10:00", 101.25, 0.3m), Exit("10:15", 100.30, 0.8m)], Taken: 1), MinProgress: 0.5m, TimeStopBars: 2),

        // P21: the swing time stop at the end of session 1 (minimum progress 1.5) with the add open: the close of 15:29 is 101.25, progress 1.25: out there. R = 0.625
        new PyramidRow("P21 the swing time stop with an add open", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25"], [B("10:00", 101.20), B("10:15", 101.25)],
            new PyramidWant(ExitReason.TimeStop, "15:29", 101.25, 0.625, 0, 1.20, [Entry(), Add("10:00", 101.25, 0.3m), Exit("15:29", 101.25, 0.8m)], Taken: 1),
            Hold: 2, MinProgress: 1.5m, TimeStopSessions: 1),

        // P23: the mirror of P2 as a sell: add 1 at 98.75 (S = 100.78125), add 2 (0.2) at 97.65 after a close at 97.70 (limit Up(97.6023) = 97.65): S_req = (99.155 + 1.0) / 1.0 = 100.155; square-off at 97.00: R = -(97.00 - 99.155) = 2.155
        new PyramidRow("P23 a sell with two adds", Side.Sell,
            ["09:59 100.00/100.00/98.80/98.80", "10:00 98.75/98.75/98.75/98.75", "10:14 98.75/98.75/97.70/97.70", "10:15 97.65/97.65/97.65/97.65", "10:29 97.65/97.65/97.00/97.00"],
            [B("10:00", 98.80), B("10:15", 97.70), B("10:30", 97.00)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 97.00, 2.155, 0, 2.155, [Entry(), Add("10:00", 98.75, 0.3m), Add("10:15", 97.65, 0.2m), Exit("15:15", 97.00, 1.0m)], Taken: 2)),

        // P24: a sell whose next open 98.65 is below the limit 98.75: missed. Square-off at 98.65: R = -(0.5 x 98.65 - 50) = 0.675
        new PyramidRow("P24 a sell add missed by the limit", Side.Sell,
            ["09:59 100.00/100.00/98.80/98.80", "10:00 98.65/98.65/98.65/98.65"], [B("10:00", 98.80), B("10:15", 98.65)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 98.65, 0.675, 0, 1.35, [Entry(), Exit("15:15", 98.65, 0.5m)], Missed: 1)),

        // P25: P1 with slippage: entry 100.05; add 101.25 + 0.05 = 101.30 (the limit); the stop 99.21875 in a minute of range 2.05 pays (1 + 2) x 0.05 + 0.05 x 2.05 = 0.2525: Down(98.96625) = 98.95.
        // RFilled = 0.8 x 98.95 - (0.5 x 100.05 + 0.3 x 101.30) = 79.16 - 80.415 = -1.255
        new PyramidRow("P25 slippage on the entry, the add and the stop", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:20 101.25/101.25/99.20/99.30"], [B("10:00", 101.20), B("10:15", 101.25)],
            new PyramidWant(ExitReason.Stop, "10:20", 99.21875, -1.00, 1.25, 1.20, [Entry(filled: 100.05), Add("10:00", 101.25, 0.3m, 101.30), Exit("10:20", 99.21875, 0.8m)], RFilled: -1.255, Taken: 1), Modelled: true),

        // P27: the mirror of P12: after add 1 the stop is 100.78125 and 10:30 opens 101.10 above it: out at the open. R = -(0.8 x 101.10 - 79.625) = -1.255; MAE 101.10 - 99.53125 = 1.56875
        new PyramidRow("P27 a sell gaps through the combined stop", Side.Sell,
            ["09:59 100.00/100.00/98.80/98.80", "10:00 98.75/98.75/98.75/98.75", "10:30 101.10/101.20/101.00/101.10"], [B("10:00", 98.80), B("10:15", 98.75)],
            new PyramidWant(ExitReason.Stop, "10:30", 101.10, -1.255, 1.56875, 1.20, [Entry(), Add("10:00", 98.75, 0.3m), Exit("10:30", 101.10, 0.8m)], Taken: 1, StopGap: true)),

        // P28: the list ends at 10:30 with the add open: the close of the last minute, 101.25: R = 0.625
        new PyramidRow("P28 the end of the data with an add open", Side.Buy,
            ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25"], [B("10:00", 101.20), B("10:15", 101.25)],
            new PyramidWant(ExitReason.EndOfData, "10:30", 101.25, 0.625, 0, 1.20, [Entry(), Add("10:00", 101.25, 0.3m), Exit("10:30", 101.25, 0.8m)], Taken: 1), EndsAfter: "10:30"),

        // P31: a close exactly at the level (progress 1.00) asks for the add: limit Down(101.101) = 101.10, the open 101.05 fills; square-off at 101.05: R = 0.8 x 101.05 - 80.315 = 0.525; MFE 1.00 (segment 1)
        new PyramidRow("P31 a close exactly at the level attempts the add", Side.Buy,
            ["09:59 100.00/101.00/100.00/101.00", "10:00 101.05/101.05/101.05/101.05"], [B("10:00", 101.00)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 101.05, 0.525, 0, 1.00, [Entry(), Add("10:00", 101.05, 0.3m), Exit("15:15", 101.05, 0.8m)], Taken: 1)),

        // P32: no close reaches the first level (progress 0.40): the half position leaves at the square-off 100.40: R = 0.5 x 0.40 = 0.20, MAE 0.10, MFE 0.50
        new PyramidRow("P32 no add when no close reaches the first level", Side.Buy,
            ["09:59 100.00/100.50/99.90/100.40"], [B("10:00", 100.40), B("10:15", 100.40)],
            new PyramidWant(ExitReason.SquareOff, "15:15", 100.40, 0.20, 0.10, 0.50, [Entry(), Exit("15:15", 100.40, 0.5m)])),
    ];

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_trade_is_the_one_computed_by_hand(PyramidRow row) => PyramidHand.Check(row);

    [Fact]
    public void There_are_at_least_twenty_four_rows_of_both_sides_and_both_kinds_of_walk()
    {
        var rows = Rows.Select(r => r.Data).ToList();

        rows.Count.ShouldBeGreaterThanOrEqualTo(24);
        rows.Count(r => r.Side == Side.Sell).ShouldBeGreaterThanOrEqualTo(4);
        rows.Count(r => r.Hold > 0).ShouldBeGreaterThanOrEqualTo(3);
        rows.Count(r => r.Want.Taken == 0).ShouldBeGreaterThan(3);
        rows.Count(r => r.Want.Taken == 1).ShouldBeGreaterThan(3);
        rows.Count(r => r.Want.Taken == 2).ShouldBeGreaterThanOrEqualTo(3);
        rows.Select(r => r.Want.Reason).Distinct().ShouldBe([ExitReason.Stop, ExitReason.SquareOff, ExitReason.MaxHold, ExitReason.TimeStop, ExitReason.EndOfData], ignoreOrder: true);
        rows.Count(r => r.Modelled).ShouldBe(1);
    }
}
