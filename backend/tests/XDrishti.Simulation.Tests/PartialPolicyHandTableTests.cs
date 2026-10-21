using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 2, 4 and 5 of P4-02: 21 rows of <c>PARTIAL_BE</c>, 21 of <c>PARTIAL_TRAIL</c> and 19 of the <c>time_stop</c> whose every figure was computed by hand from the rules D1 to D9 (never from the engine). The frame is that of
/// P4-01: BUY at 100.50 with the stop 98.00 (risk 2.50, one share = 1) or its mirror SELL at 99.50 with the stop 102.00, entered in the 09:45 minute at the open, no slippage; T1 at +1R (103.00 or 97.00) unless a row says otherwise.
/// A minute that is not listed repeats the previous close. The tape of a trail is written by hand (the bar closes at its time; the first bar is the decision bar, which closes at the fill instant and is never read). R is checked to 12 decimals.
/// </summary>
public sealed class PartialPolicyHandTableTests
{
    private const double Tolerance = 1e-12;

    private static readonly DateOnly Monday = DayBuilder.Monday;

    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private static readonly DateOnly Wednesday = Monday.AddDays(2);

    private const double Nan = double.NaN;

    private sealed record L(string At, double Price, decimal Share, LegKind Kind, string Reason, DateOnly? Date = null);

    private sealed record Expected(IReadOnlyList<L> Legs, ExitReason Reason, double R, double Mae, double Mfe, string? At = null, double? Price = null, DateOnly? Date = null, bool StopGap = false, int Ignored = 0, bool TimeStopAtLastClose = false, int Sessions = 1);

    private sealed record Case(string Name, Side Side, Day[] Days, ExitTape Tape, LegPolicy Policy, double Reward, Expected Expected, bool Swing = false, int Hold = 3, int SquareOff = 915);

    private static L E(string at, double price = 100.50, DateOnly? date = null) => new(at, price, 1m, LegKind.Entry, string.Empty, date);

    private static L T1(string at, double price, decimal share = 0.5m, DateOnly? date = null) => new(at, price, share, LegKind.Exit, "T1", date);

    private static L X(string at, double price, decimal share, string reason, DateOnly? date = null) => new(at, price, share, LegKind.Exit, reason, date);

    private static LegPolicy Be(decimal book = 0.5m, decimal t2 = 2.0m, decimal? min = null, int bars = 0, int sessions = 0) => new(PartialKind.Be, book, t2, 0m, min, bars, sessions);

    private static LegPolicy Trail(decimal book = 0.5m, decimal mult = 2.0m) => new(PartialKind.Trail, book, 0m, mult, null, 0, 0);

    private static LegPolicy Stop(decimal min, int bars = 0, int sessions = 0) => new(PartialKind.None, 0m, 0m, 0m, min, bars, sessions);

    private static ExitTape Tape(bool atr, params (DateOnly Date, string At, double Close, double Atr)[] rows)
    {
        var tape = new ExitTape(atr ? ["atr"] : []);
        foreach (var (date, at, close, value) in rows)
        {
            if (atr)
            {
                tape.Append(DayBuilder.Epoch(date, at), close, [value]);
            }
            else
            {
                tape.Append(DayBuilder.Epoch(date, at), close, []);
            }
        }

        return tape;
    }

    private static ExitTape TrailTape(params (string At, double Atr)[] rows) => Tape(true, [(Monday, "09:45", 100.50, Nan), .. rows.Select(r => (Monday, r.At, 103.00, r.Atr))]);

    private static ExitTape CloseTape(params (string At, double Close)[] rows) => Tape(false, [(Monday, "09:45", 100.50, Nan), .. rows.Select(r => (Monday, r.At, r.Close, Nan))]);

    private static Day BuyDay(params string[] rows) => Day.Dense().Start(100.50).Set("09:45 100.50/100.50/100.50/100.50").Set(rows);

    private static Day SellDay(params string[] rows) => Day.Dense().Start(99.50).Set("09:45 99.50/99.50/99.50/99.50").Set(rows);

    private const string BuyT1 = "10:00 100.50/103.00/100.50/103.00";

    private const string BuyT1At3 = "10:03 100.50/103.00/100.50/103.00";

    private const string SellT1 = "10:00 99.50/99.50/97.00/97.00";

    private const string SellT1At3 = "10:03 99.50/99.50/97.00/97.00";

    private static Day[] One(Day day) => [day];

    private static Day[] Swing(Func<Day, Day> monday, Func<Day, Day>? tuesday = null, Func<Day, Day>? wednesday = null, double start = 100.50)
    {
        var mon = monday(Day.Dense(Monday).Start(start).Set("09:45 " + Price(start)));
        var tue = (tuesday ?? (d => d))(Day.After(mon, Tuesday));
        var wed = (wednesday ?? (d => d))(Day.After(tue, Wednesday));
        return [mon, tue, wed];
    }

    private static string Price(double p) => p.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/" + p.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/" + p.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/" + p.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static EntryCandidate Candidate(Side side) => side == Side.Buy
        ? Cand.Make(Side.Buy, 29, "09:45", 98.00, "09:30", (99.00, 100.50, 99.00, 100.50))
        : Cand.Make(Side.Sell, 29, "09:45", 102.00, "09:30", (101.00, 101.00, 99.50, 99.50));

    private static Case Buy(string name, Day[] days, ExitTape tape, LegPolicy policy, double reward, Expected expected, bool swing = false, int hold = 3) => new(name, Side.Buy, days, tape, policy, reward, expected, swing, hold);

    private static Case Sell(string name, Day[] days, ExitTape tape, LegPolicy policy, double reward, Expected expected, bool swing = false, int hold = 3) => new(name, Side.Sell, days, tape, policy, reward, expected, swing, hold);

    private static ExitTape None => Tape(false, (Monday, "09:45", 100.50, Nan));

    // ---- PARTIAL_BE: 21 rows -----------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static IEnumerable<Case> BeRows()
    {
        // B1 (D3 a): half at T1 103.00, the stop moves to 100.50, the rest at T2 105.50: R = (0.5 x 2.50 + 0.5 x 5.00) / 2.50 = 1.5; MFE (105.50 - 100.50) / 2.50 = 2.0
        yield return Buy("B01 T2 reached", One(BuyDay(BuyT1, "10:10 103.00/105.50/103.00/105.50")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("10:10", 105.50, 0.5m, "Target")], ExitReason.Target, 1.5, 0.0, 2.0));

        // B2 (D3 b): back to 100.50: R = (1.25 + 0) / 2.50 = 0.5
        yield return Buy("B02 back to break-even", One(BuyDay(BuyT1, "10:10 103.00/103.00/100.50/100.50")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("10:10", 100.50, 0.5m, "Stop")], ExitReason.Stop, 0.5, 0.0, 1.0));

        // B3 (D3 c): the initial stop before T1: one fill, R = -1, MAE 1
        yield return Buy("B03 initial stop before T1", One(BuyDay("10:00 100.50/100.50/98.00/98.00")), None, Be(), 1.0,
            new([], ExitReason.Stop, -1.0, 1.0, 0.0, "10:00", 98.00));

        // B4 (D3 d): square-off after T1 at 100.60: R = (1.25 + 0.5 x 0.10) / 2.50 = 0.52
        yield return Buy("B04 square-off after T1", One(BuyDay(BuyT1, "10:10 103.00/103.00/100.60/100.60")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("15:15", 100.60, 0.5m, "SquareOff")], ExitReason.SquareOff, 0.52, 0.0, 1.0));

        // B5: MAX_HOLD after T1 (swing, 3 sessions): the rest leaves at the close of Wednesday 15:29 (103.00): R = (1.25 + 0.5 x 2.50) / 2.50 = 1.0
        yield return Buy("B05 MaxHold after T1", Swing(m => m.Set(BuyT1)), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("15:29", 103.00, 0.5m, "MaxHold", Wednesday)], ExitReason.MaxHold, 1.0, 0.0, 1.0, Sessions: 3), swing: true);

        // B6: the data end at 11:00 after T1: the rest leaves at the last close 103.00: R = 1.0
        yield return Buy("B06 end of data after T1", One(Day.Dense(Monday, "09:15", "11:00").Start(100.50).Set("09:45 100.50/100.50/100.50/100.50").Set(BuyT1)), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("11:00", 103.00, 0.5m, "EndOfData")], ExitReason.EndOfData, 1.0, 0.0, 1.0));

        // B7: a gap through the break-even stop: 10:10 opens 100.00: the rest at the open: R = (1.25 - 0.25) / 2.50 = 0.4; MAE (100.50 - 100.00) / 2.50 = 0.2
        yield return Buy("B07 gap through break-even", One(BuyDay(BuyT1, "10:10 100.00/100.00/99.50/99.50")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("10:10", 100.00, 0.5m, "Stop")], ExitReason.Stop, 0.4, 0.2, 1.0, StopGap: true));

        // B8: T1 and the initial stop in one minute (open 100.50 between them): the stop, for the whole position, ambiguous
        yield return Buy("B08 T1 and the initial stop in one minute", One(BuyDay("10:00 100.50/103.00/98.00/100.00")), None, Be(), 1.0,
            new([], ExitReason.Stop, -1.0, 1.0, 0.0, "10:00", 98.00));

        // B9: T1 at the open of a gap (103.50): filled at 103.00 and never at a better price; the minute counts in full for the excursions (its high is 104.00: MFE 1.4); then back to 100.50: R = 0.5
        yield return Buy("B09 T1 at the open of a gap", One(BuyDay("10:00 103.50/104.00/103.50/103.50", "10:10 103.50/103.50/100.50/100.50")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("10:10", 100.50, 0.5m, "Stop")], ExitReason.Stop, 0.5, 0.0, 1.4));

        // B10 (sell of B1): SELL 99.50, stop 102.00, T1 97.00, T2 94.50: R = (1.25 + 2.5) / 2.50 = 1.5
        yield return Sell("B10 sell T2 reached", One(SellDay(SellT1, "10:10 97.00/97.00/94.50/94.50")), None, Be(), 1.0,
            new([E("09:45", 99.50), T1("10:00", 97.00), X("10:10", 94.50, 0.5m, "Target")], ExitReason.Target, 1.5, 0.0, 2.0));

        // B11 (sell of B2): back to 99.50
        yield return Sell("B11 sell back to break-even", One(SellDay(SellT1, "10:10 97.00/99.50/97.00/99.50")), None, Be(), 1.0,
            new([E("09:45", 99.50), T1("10:00", 97.00), X("10:10", 99.50, 0.5m, "Stop")], ExitReason.Stop, 0.5, 0.0, 1.0));

        // B12 (sell of B3)
        yield return Sell("B12 sell initial stop before T1", One(SellDay("10:00 99.50/102.00/99.50/102.00")), None, Be(), 1.0,
            new([], ExitReason.Stop, -1.0, 1.0, 0.0, "10:00", 102.00));

        // B13 (sell of B7): 10:10 opens 100.00 above the break-even stop 99.50: R = (1.25 - 0.25) / 2.50 = 0.4
        yield return Sell("B13 sell gap through break-even", One(SellDay(SellT1, "10:10 100.00/100.50/100.00/100.50")), None, Be(), 1.0,
            new([E("09:45", 99.50), T1("10:00", 97.00), X("10:10", 100.00, 0.5m, "Stop")], ExitReason.Stop, 0.4, 0.2, 1.0, StopGap: true));

        // B14 (sell of B4): flat 99.00, square-off at 99.00: R = (1.25 + 0.5 x 0.50) / 2.50 = 0.6
        yield return Sell("B14 sell square-off after T1", One(SellDay(SellT1, "10:10 97.00/99.00/97.00/99.00")), None, Be(), 1.0,
            new([E("09:45", 99.50), T1("10:00", 97.00), X("15:15", 99.00, 0.5m, "SquareOff")], ExitReason.SquareOff, 0.6, 0.0, 1.0));

        // B15: swing, T1 on Monday and Tuesday opens 100.00 through the break-even stop: R = 0.4, two sessions
        yield return Buy("B15 swing gap through break-even", Swing(m => m.Set(BuyT1), t => t.Set("09:15 100.00/100.00/99.50/99.50")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("09:15", 100.00, 0.5m, "Stop", Tuesday)], ExitReason.Stop, 0.4, 0.2, 1.0, StopGap: true, Sessions: 2), swing: true);

        // B16: swing, T1 on Monday and T2 on Tuesday 10:00: R = 1.5
        yield return Buy("B16 swing T2 on the next day", Swing(m => m.Set(BuyT1), t => t.Set("10:00 103.00/105.50/103.00/105.50")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("10:00", 105.50, 0.5m, "Target", Tuesday)], ExitReason.Target, 1.5, 0.0, 2.0, Sessions: 2), swing: true);

        // B17 (D2): the minute that fills T1 also reaches T2 (105.50) and is not a T2 fill: T2 fills in the next minute, which opens at 105.50
        yield return Buy("B17 T1 minute is never also T2", One(BuyDay("10:00 100.50/105.50/100.50/105.50")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("10:01", 105.50, 0.5m, "Target")], ExitReason.Target, 1.5, 0.0, 2.0));

        // B18 (D2): T1 at the open of the minute 103.50, the minute trades down to 98.00 and closes 100.00: T1 is filled (the first print), the minute counts in full (MAE 1.0, MFE 1.4) and is not a break-even stop; 10:01 opens 100.00 through 100.50: R = (1.25 - 0.25) / 2.50 = 0.4
        yield return Buy("B18 T1 minute is never also the break-even stop", One(BuyDay("10:00 103.50/104.00/98.00/100.00")), None, Be(), 1.0,
            new([E("09:45"), T1("10:00", 103.00), X("10:01", 100.00, 0.5m, "Stop")], ExitReason.Stop, 0.4, 1.0, 1.4, StopGap: true));

        // B19: a quarter at T1, three quarters at T2: R = (0.25 x 2.50 + 0.75 x 5.00) / 2.50 = 1.75
        yield return Buy("B19 a quarter booked", One(BuyDay(BuyT1, "10:10 103.00/105.50/103.00/105.50")), None, Be(0.25m), 1.0,
            new([E("09:45"), T1("10:00", 103.00, 0.25m), X("10:10", 105.50, 0.75m, "Target")], ExitReason.Target, 1.75, 0.0, 2.0));

        // B20: T1 at 0.6R = 102.00, T2 at 1.6R = 104.50: R = (0.5 x 1.50 + 0.5 x 4.00) / 2.50 = 1.1; MFE 1.6
        yield return Buy("B20 T1 at 0.6R and T2 at 1.6R", One(BuyDay("10:00 100.50/102.00/100.50/102.00", "10:10 102.00/104.50/102.00/104.50")), None, Be(0.5m, 1.6m), 0.6,
            new([E("09:45"), T1("10:00", 102.00), X("10:10", 104.50, 0.5m, "Target")], ExitReason.Target, 1.1, 0.0, 1.6));

        // B21 (X5): the entry is filled at the limit 100.60 (the minute opens 100.80), risk 2.60, T1 = 103.20; the fill minute reaches 104.00 but the target is not checked in it; then flat 101.00 to the square-off: one fill, R = 0.40 / 2.60; MAE 0; MFE counts the open of the square-off only
        yield return Buy("B21 the fill minute at the limit does not fill T1", One(Day.Dense().Start(100.80).Set("09:45 100.80/104.00/100.60/101.00")), None, Be(), 1.0,
            new([], ExitReason.SquareOff, 0.4 / 2.6, 0.0, 0.4 / 2.6, "15:15", 101.00));
    }

    // ---- PARTIAL_TRAIL: 21 rows ---------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static IEnumerable<Case> TrailRows()
    {
        // R1 (D6): T1 at 10:03; the bar closing 10:15 has the highest high since T1 103.00 and ATR 0.50: level 103.00 - 2 x 0.50 = 102.00, in force from 10:15; 10:20 trades to 101.50: the rest at 102.00; R = (1.25 + 0.5 x 1.50) / 2.50 = 0.8
        yield return Buy("R01 the worked trail", One(BuyDay(BuyT1At3, "10:20 103.00/103.00/101.50/101.50")), TrailTape(("10:15", 0.5)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:20", 102.00, 0.5m, "Stop")], ExitReason.Stop, 0.8, 0.0, 1.0));

        // R2: an ATR that grows: the bar closing 10:30 would put the stop at 103.00 - 2.00 = 101.00, looser than 102.00: ignored and counted; the exit is at 102.00
        yield return Buy("R02 a looser level is ignored and counted", One(BuyDay(BuyT1At3, "10:35 103.00/103.00/101.50/101.50")), TrailTape(("10:15", 0.5), ("10:30", 1.0)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:35", 102.00, 0.5m, "Stop")], ExitReason.Stop, 0.8, 0.0, 1.0, Ignored: 1));

        // R3: a NaN ATR gives no level: the initial stop 98.00 stays and the rest leaves there: R = (1.25 - 1.25) / 2.50 = 0; MAE 1.0
        yield return Buy("R03 a NaN ATR moves nothing", One(BuyDay(BuyT1At3, "10:20 103.00/103.00/97.00/97.00")), TrailTape(("10:15", Nan)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:20", 98.00, 0.5m, "Stop")], ExitReason.Stop, 0.0, 1.0, 1.0));

        // R4: the highest high since T1 is 104.40: level 104.40 - 1.00 = 103.40; 10:20 trades to 103.00: the rest at 103.40: R = (1.25 + 0.5 x 2.90) / 2.50 = 1.08; MFE 1.56
        yield return Buy("R04 the anchor is the highest high since T1", One(BuyDay(BuyT1At3, "10:05 103.00/104.40/103.00/104.00", "10:20 104.00/104.00/103.00/103.00")), TrailTape(("10:15", 0.5)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:20", 103.40, 0.5m, "Stop")], ExitReason.Stop, 1.08, 0.0, 1.56));

        // R5: T1 in the last minute of the bar (10:14): the bar closing 10:15 is after the T1 minute, its anchor is that minute's high 103.50: level 102.50; 10:16 trades to 102.00: R = (1.25 + 0.5 x 2.00) / 2.50 = 0.9; MFE 1.2
        yield return Buy("R05 the bar that closes on the minute after T1", One(BuyDay("10:14 100.50/103.50/100.50/103.00", "10:16 103.00/103.00/102.00/102.00")), TrailTape(("10:15", 0.5)), Trail(), 1.0,
            new([E("09:45"), T1("10:14", 103.00), X("10:16", 102.50, 0.5m, "Stop")], ExitReason.Stop, 0.9, 0.0, 1.2));

        // R6: T1 at 10:15, the minute that the bar closing 10:15 ends at: that bar is not after T1 and gives no level (it would be 102.50 with ATR 0.25); the bar 10:30 gives 103.00 - 1.00 = 102.00; 10:20 (low 102.30) does not stop; 10:35 (low 101.90) does: R = 0.8
        yield return Buy("R06 a bar that closes at the T1 minute gives no level", One(BuyDay("10:15 100.50/103.00/100.50/103.00", "10:20 103.00/103.00/102.30/102.30", "10:35 102.30/102.30/101.90/101.90")), TrailTape(("10:15", 0.25), ("10:30", 0.5)), Trail(), 1.0,
            new([E("09:45"), T1("10:15", 103.00), X("10:35", 102.00, 0.5m, "Stop")], ExitReason.Stop, 0.8, 0.0, 1.0));

        // R7 (sell of R1): lowest low since T1 97.00 + 1.00 = 98.00; 10:20 trades to 98.50: R = (1.25 + 0.5 x 1.50) / 2.50 = 0.8
        yield return Sell("R07 sell the worked trail", One(SellDay(SellT1At3, "10:20 97.00/98.50/97.00/98.50")), TrailTape(("10:15", 0.5)), Trail(), 1.0,
            new([E("09:45", 99.50), T1("10:03", 97.00), X("10:20", 98.00, 0.5m, "Stop")], ExitReason.Stop, 0.8, 0.0, 1.0));

        // R8 (sell of R2): the bar 10:30 would give 97.00 + 2.00 = 99.00, looser than 98.00: ignored and counted
        yield return Sell("R08 sell a looser level is ignored", One(SellDay(SellT1At3, "10:35 97.00/98.50/97.00/98.50")), TrailTape(("10:15", 0.5), ("10:30", 1.0)), Trail(), 1.0,
            new([E("09:45", 99.50), T1("10:03", 97.00), X("10:35", 98.00, 0.5m, "Stop")], ExitReason.Stop, 0.8, 0.0, 1.0, Ignored: 1));

        // R9 (sell of R3): NaN: the initial stop 102.00; R = 0
        yield return Sell("R09 sell a NaN ATR moves nothing", One(SellDay(SellT1At3, "10:20 97.00/102.00/97.00/102.00")), TrailTape(("10:15", Nan)), Trail(), 1.0,
            new([E("09:45", 99.50), T1("10:03", 97.00), X("10:20", 102.00, 0.5m, "Stop")], ExitReason.Stop, 0.0, 1.0, 1.0));

        // R10: a gap through the trailed stop 102.00: 10:20 opens 101.00: the rest at the open: R = (1.25 + 0.5 x 0.50) / 2.50 = 0.6
        yield return Buy("R10 a gap through the trailed stop", One(BuyDay(BuyT1At3, "10:20 101.00/101.00/100.50/100.50")), TrailTape(("10:15", 0.5)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:20", 101.00, 0.5m, "Stop")], ExitReason.Stop, 0.6, 0.0, 1.0, StopGap: true));

        // R11: T1 and the initial stop in one minute: the stop, for the whole, R = -1
        yield return Buy("R11 T1 and the initial stop in one minute", One(BuyDay("10:00 100.50/103.00/98.00/100.00")), TrailTape(("10:15", 0.5)), Trail(), 1.0,
            new([], ExitReason.Stop, -1.0, 1.0, 0.0, "10:00", 98.00));

        // R12: ATR 0.05: level 102.90, never reached by the flat 103.00: the square-off open 103.00: R = 1.0
        yield return Buy("R12 a trail that is never reached", One(BuyDay(BuyT1At3)), TrailTape(("10:15", 0.05), ("10:30", 0.05)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("15:15", 103.00, 0.5m, "SquareOff")], ExitReason.SquareOff, 1.0, 0.0, 1.0));

        // R13: the data ends at 11:00: the last close 103.00
        yield return Buy("R13 end of data after T1", One(Day.Dense(Monday, "09:15", "11:00").Start(100.50).Set("09:45 100.50/100.50/100.50/100.50").Set(BuyT1At3)), TrailTape(("10:15", 0.05)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("11:00", 103.00, 0.5m, "EndOfData")], ExitReason.EndOfData, 1.0, 0.0, 1.0));

        // R14: swing, a trail across the session break: the bar that closes Monday 15:30 has the anchor 103.00 and gives 102.00 from Tuesday 09:15, which opens 101.00: the rest at the open, R = 0.6
        yield return Buy("R14 a level of Monday acts on the first minute of Tuesday", Swing(m => m.Set(BuyT1At3), t => t.Set("09:15 101.00/101.00/100.50/100.50")), Tape(true, (Monday, "09:45", 100.50, Nan), (Monday, "15:30", 103.00, 0.5)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("09:15", 101.00, 0.5m, "Stop", Tuesday)], ExitReason.Stop, 0.6, 0.0, 1.0, StopGap: true, Sessions: 2), swing: true);

        // R15: swing, the anchor goes on across the break: Tuesday's high 104.50 gives, with the bar closing 10:15 (ATR 0.50), 103.50; 10:20 trades to 103.00: R = (1.25 + 0.5 x 3.00) / 2.50 = 1.1; MFE 1.6
        yield return Buy("R15 the anchor goes on across the session break", Swing(m => m.Set(BuyT1At3), t => t.Set("10:00 103.00/104.50/103.00/104.00", "10:20 104.00/104.00/103.00/103.00")), Tape(true, (Monday, "09:45", 100.50, Nan), (Monday, "15:30", 103.00, 0.5), (Tuesday, "10:15", 103.00, 0.5)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:20", 103.50, 0.5m, "Stop", Tuesday)], ExitReason.Stop, 1.1, 0.0, 1.6, Sessions: 2), swing: true);

        // R16: a multiple of 0.5: level 103.00 - 0.25 = 102.75; 10:20 trades to 102.70: R = (1.25 + 0.5 x 2.25) / 2.50 = 0.95
        yield return Buy("R16 a multiple of 0.5", One(BuyDay(BuyT1At3, "10:20 103.00/103.00/102.70/102.70")), TrailTape(("10:15", 0.5)), Trail(0.5m, 0.5m), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:20", 102.75, 0.5m, "Stop")], ExitReason.Stop, 0.95, 0.0, 1.0));

        // R17: a quarter booked: R = (0.25 x 2.50 + 0.75 x 1.50) / 2.50 = 0.7
        yield return Buy("R17 a quarter booked", One(BuyDay(BuyT1At3, "10:20 103.00/103.00/101.50/101.50")), TrailTape(("10:15", 0.5)), Trail(0.25m), 1.0,
            new([E("09:45"), T1("10:03", 103.00, 0.25m), X("10:20", 102.00, 0.75m, "Stop")], ExitReason.Stop, 0.7, 0.0, 1.0));

        // R18: T1 at the open of a gap (103.50, high 104.00): filled at 103.00, anchor 104.00, level 103.00; 10:20 trades to 102.90 from the open 103.50: the rest at 103.00: R = (1.25 + 0.5 x 2.50) / 2.50 = 1.0; MFE 1.4
        yield return Buy("R18 T1 at the open of a gap", One(BuyDay("10:03 103.50/104.00/103.50/103.50", "10:20 103.50/103.50/102.90/102.90")), TrailTape(("10:15", 0.5)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:20", 103.00, 0.5m, "Stop")], ExitReason.Stop, 1.0, 0.0, 1.4));

        // R19: swing with a hold of 2 sessions: a trail never reached, the rest leaves at Tuesday 15:29 (MaxHold) at 103.00: R = 1.0
        yield return Buy("R19 MaxHold after T1", Swing(m => m.Set(BuyT1At3)), Tape(true, (Monday, "09:45", 100.50, Nan), (Monday, "15:30", 103.00, 0.05)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("15:29", 103.00, 0.5m, "MaxHold", Tuesday)], ExitReason.MaxHold, 1.0, 0.0, 1.0, Sessions: 2), swing: true, hold: 2);

        // R20: ATR 0: the level is the anchor itself, 103.00, which the flat minute 10:15 touches: the rest at 103.00 (not a gap): R = 1.0
        yield return Buy("R20 a zero ATR puts the stop at the anchor", One(BuyDay(BuyT1At3)), TrailTape(("10:15", 0.0)), Trail(), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:15", 103.00, 0.5m, "Stop")], ExitReason.Stop, 1.0, 0.0, 1.0));

        // R21: swing, a sell: lowest low 97.00 + 1.00 = 98.00 from Tuesday 09:15, which opens 99.00: the rest at the open: R = (1.25 + 0.5 x 0.50) / 2.50 = 0.6
        yield return Sell("R21 sell a level of Monday acts on the first minute of Tuesday", Swing(m => m.Set(SellT1At3), t => t.Set("09:15 99.00/99.00/98.50/98.50"), start: 99.50), Tape(true, (Monday, "09:45", 99.50, Nan), (Monday, "15:30", 97.00, 0.5)), Trail(), 1.0,
            new([E("09:45", 99.50), T1("10:03", 97.00), X("09:15", 99.00, 0.5m, "Stop", Tuesday)], ExitReason.Stop, 0.6, 0.0, 1.0, StopGap: true, Sessions: 2), swing: true);
    }

    // ---- time_stop: 19 rows -------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static Case[] Rows { get; } = [.. BeRows(), .. TrailRows(), .. StopRows()];

    private static ExitTape Every15(double close)
    {
        var rows = new List<(DateOnly, string, double, double)> { (Monday, "09:45", 100.50, Nan) };
        for (var minute = DayBuilder.MinuteOfDay("10:00"); minute <= DayBuilder.MinuteOfDay("15:15"); minute += 15)
        {
            rows.Add((Monday, (minute / 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture), close, Nan));
        }

        return Tape(false, [.. rows]);
    }

    private static IEnumerable<Case> StopRows()
    {
        // S1 (D8): N = 1, the bar closing 10:00 closes at 100.90: progress 0.40 / 2.50 = 0.16 < 0.5: the position leaves at the open of 10:00, 100.80: R = 0.12; MFE counts that open
        yield return Buy("S01 N=1 progress below", One(BuyDay("10:00 100.80/101.00/100.70/100.90")), CloseTape(("10:00", 100.90)), Stop(0.5m, 1), 2.0,
            new([], ExitReason.TimeStop, 0.12, 0.0, 0.12, "10:00", 100.80));

        // S2: progress equal to the minimum (101.75: 1.25 / 2.50 = 0.5): no exit; the target 105.50 at 10:30: R = 2.0
        yield return Buy("S02 N=1 progress equal", One(BuyDay("10:30 101.75/105.50/101.75/105.50")), CloseTape(("10:00", 101.75)), Stop(0.5m, 1), 2.0,
            new([], ExitReason.Target, 2.0, 0.0, 2.0, "10:30", 105.50));

        // S3: progress above (102.00: 0.6): no exit; the square-off open 102.00: R = 0.6
        yield return Buy("S03 N=1 progress above", One(BuyDay("09:59 100.50/102.00/100.50/102.00")), CloseTape(("10:00", 102.00)), Stop(0.5m, 1), 2.0,
            new([], ExitReason.SquareOff, 0.6, 0.0, 0.6, "15:15", 102.00));

        // S4: N = 2: the first bar (close 100.00) is not judged; the second (103.00) is above: spent; the square-off open 103.00: R = 1.0; MAE 0.2
        yield return Buy("S04 N=2 the first bar is not judged", One(BuyDay("09:59 100.50/100.50/100.00/100.00", "10:14 100.00/103.00/100.00/103.00")), CloseTape(("10:00", 100.00), ("10:15", 103.00)), Stop(0.5m, 2), 2.0,
            new([], ExitReason.SquareOff, 1.0, 0.2, 1.0, "15:15", 103.00));

        // S5: N = 2: the second bar closes 100.60 (0.04): the position leaves at the open of 10:15, 100.70: R = 0.08; MFE 1.0 (the high 103.00 of the minute 09:59)
        yield return Buy("S05 N=2 progress below at the second bar", One(BuyDay("09:59 100.50/103.00/100.50/103.00", "10:14 103.00/103.00/100.60/100.60", "10:15 100.70/100.80/100.60/100.70")), CloseTape(("10:00", 103.00), ("10:15", 100.60)), Stop(0.5m, 2), 2.0,
            new([], ExitReason.TimeStop, 0.08, 0.0, 1.0, "10:15", 100.70));

        // S6: N = 22: the bar closes at 15:15 = the square-off: no check, the square-off exits first (open 100.50): R = 0
        yield return Buy("S06 N past the square-off", One(BuyDay()), Every15(100.00), Stop(0.5m, 22), 2.0,
            new([], ExitReason.SquareOff, 0.0, 0.0, 0.0, "15:15", 100.50));

        // S7: N = 21: the bar closes at 15:00 (100.00, progress -0.2): the position leaves at the open of 15:00 (100.50): R = 0, reason TimeStop
        yield return Buy("S07 N before the square-off", One(BuyDay()), Every15(100.00), Stop(0.5m, 21), 2.0,
            new([], ExitReason.TimeStop, 0.0, 0.0, 0.0, "15:00", 100.50));

        // S8: the initial stop before the check: R = -1
        yield return Buy("S08 the stop comes first", One(BuyDay("10:05 100.50/100.50/98.00/98.00")), CloseTape(("10:00", 100.00), ("10:15", 100.00)), Stop(0.5m, 2), 2.0,
            new([], ExitReason.Stop, -1.0, 1.0, 0.0, "10:05", 98.00));

        // S9: the check minute opens 97.50 through the stop 98.00: the stop row takes it (a gap), not the time stop: R = -3.00 / 2.50 = -1.2
        yield return Buy("S09 a gap through the stop at the check minute", One(BuyDay("10:00 97.50/97.50/97.00/97.00")), CloseTape(("10:00", 100.60)), Stop(0.5m, 1), 2.0,
            new([], ExitReason.Stop, -1.2, 1.2, 0.0, "10:00", 97.50, StopGap: true));

        // S10: the check minute opens 105.60 beyond the target 105.50: the target fills at 105.50, not the time stop: R = 2.0
        yield return Buy("S10 the target at the open of the check minute", One(BuyDay("10:00 105.60/106.00/105.60/106.00")), CloseTape(("10:00", 100.60)), Stop(0.5m, 1), 2.0,
            new([], ExitReason.Target, 2.0, 0.0, 2.0, "10:00", 105.50));

        // S11 (sell of S1): the bar closes 99.10: progress (99.50 - 99.10) / 2.50 = 0.16: out at the open 99.20: R = 0.12
        yield return Sell("S11 sell progress below", One(SellDay("10:00 99.20/99.30/99.10/99.10")), Tape(false, (Monday, "09:45", 99.50, Nan), (Monday, "10:00", 99.10, Nan)), Stop(0.5m, 1), 2.0,
            new([], ExitReason.TimeStop, 0.12, 0.0, 0.12, "10:00", 99.20));

        // S12 (sell of S3): the bar closes 98.00 (0.6): no exit; the square-off open 98.00: R = 0.6
        yield return Sell("S12 sell progress above", One(SellDay("09:59 99.50/99.50/98.00/98.00")), Tape(false, (Monday, "09:45", 99.50, Nan), (Monday, "10:00", 98.00, Nan)), Stop(0.5m, 1), 2.0,
            new([], ExitReason.SquareOff, 0.6, 0.0, 0.6, "15:15", 98.00));

        // S13: a minimum of 0 and a close below the entry (100.00): progress -0.2 < 0: out at the open 100.10: R = -0.16; MAE 0.16
        yield return Buy("S13 minimum 0 progress negative", One(BuyDay("10:00 100.10/100.20/100.00/100.00")), CloseTape(("10:00", 100.00)), Stop(0m, 1), 2.0,
            new([], ExitReason.TimeStop, -0.16, 0.16, 0.0, "10:00", 100.10));

        // S14: a minimum of 0 and a close at the entry: progress 0 is not below 0: no exit; the square-off open 100.50: R = 0
        yield return Buy("S14 minimum 0 progress zero", One(BuyDay()), CloseTape(("10:00", 100.50)), Stop(0m, 1), 2.0,
            new([], ExitReason.SquareOff, 0.0, 0.0, 0.0, "15:15", 100.50));

        // S15: a partial before the check: T1 103.00 at 10:03, the second bar (10:15) closes 100.60: the rest leaves at the open of 10:15, 100.70: R = (1.25 + 0.5 x 0.20) / 2.50 = 0.54
        yield return Buy("S15 a partial before the time stop", One(BuyDay(BuyT1At3, "10:10 103.00/103.00/100.60/100.60", "10:15 100.70/100.80/100.60/100.70")), CloseTape(("10:00", 103.00), ("10:15", 100.60)), Be(0.5m, 2.0m, 0.5m, 2), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("10:15", 100.70, 0.5m, "TimeStop")], ExitReason.TimeStop, 0.54, 0.0, 1.0));

        // S16 (D9): swing, 2 sessions: Tuesday's last minute 15:29 closes 100.60 (0.04): the position leaves at that close: R = 0.04
        yield return Buy("S16 swing at the last minute of the session", Swing(m => m, t => t.Set("15:29 100.50/100.60/100.50/100.60")), None, Stop(0.5m, sessions: 2), 2.0,
            new([], ExitReason.TimeStop, 0.04, 0.0, 0.04, "15:29", 100.60, Tuesday, Sessions: 2), swing: true);

        // S17 (D9, second rule): Tuesday has no minute after 14:00: the check is made at the last close 14:00 (100.60) when Wednesday's first minute shows the session has ended
        yield return Buy("S17 swing session without a minute at its end", [Day.Dense(Monday).Start(100.50).Set("09:45 100.50/100.50/100.50/100.50"), Day.Dense(Tuesday, "09:15", "14:00").Start(100.50).Set("14:00 100.50/100.60/100.50/100.60"), Day.Dense(Wednesday).Start(100.60)], None, Stop(0.5m, sessions: 2), 2.0,
            new([], ExitReason.TimeStop, 0.04, 0.0, 0.04, "14:00", 100.60, Tuesday, TimeStopAtLastClose: true, Sessions: 2), swing: true);

        // S18: swing, progress 0.6 at Tuesday's last minute: the overlay is spent; MaxHold at Wednesday's close 102.00: R = 0.6
        yield return Buy("S18 swing progress above is spent", Swing(m => m, t => t.Set("15:29 100.50/102.00/100.50/102.00")), None, Stop(0.5m, sessions: 2), 2.0,
            new([], ExitReason.MaxHold, 0.6, 0.0, 0.6, "15:29", 102.00, Wednesday, Sessions: 3), swing: true);

        // S19: swing, a partial before: T1 on Monday, flat 100.60 after; Tuesday 15:29 closes 100.60 (0.04): the rest leaves at that close: R = (1.25 + 0.5 x 0.10) / 2.50 = 0.52
        yield return Buy("S19 swing with a partial before the time stop", Swing(m => m.Set(BuyT1At3, "10:10 103.00/103.00/100.60/100.60")), None, Be(0.5m, 2.0m, 0.5m, 0, 2), 1.0,
            new([E("09:45"), T1("10:03", 103.00), X("15:29", 100.60, 0.5m, "TimeStop", Tuesday)], ExitReason.TimeStop, 0.52, 0.0, 1.0, Sessions: 2), swing: true);
    }

    public static TheoryData<string> Names { get; } = [.. Rows.Select(r => r.Name)];

    [Fact]
    public void The_tables_have_the_rows_the_spec_asks_for_20_BE_20_trail_16_time_stop()
    {
        Rows.Count(r => r.Name.StartsWith('B')).ShouldBeGreaterThanOrEqualTo(20);
        Rows.Count(r => r.Name.StartsWith('R')).ShouldBeGreaterThanOrEqualTo(20);
        Rows.Count(r => r.Name.StartsWith('S')).ShouldBeGreaterThanOrEqualTo(16);
        Rows.Select(r => r.Name).Distinct().Count().ShouldBe(Rows.Length);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_row_is_worked_by_hand(string name)
    {
        var row = Rows.Single(r => r.Name == name);
        var bars = Day.Join(row.Days);
        var fills = Cand.NoSlippage;
        var rules = row.Swing
            ? new WalkRules(row.Reward, WalkRules.NoSquareOff, fills, Cand.Tick05, SwingHold.Create(row.Hold, TestCalendars.DefaultWeek()).Value)
            : new WalkRules(row.Reward, row.SquareOff, fills, Cand.Tick05);

        var walked = TradeWalker.Walk(Candidate(row.Side), bars, rules, new ExitContext(null, row.Tape, row.Policy));

        walked.IsSuccess.ShouldBeTrue(walked.IsFailure ? walked.Error.Message : string.Empty);
        var outcome = walked.Value;
        var trade = outcome.Trade.ShouldNotBeNull();
        var expected = row.Expected;
        trade.ExitReason.ShouldBe(expected.Reason);
        trade.RExact.ShouldBe(expected.R, Tolerance, "RExact");
        trade.RFilled.ShouldBe(expected.R, Tolerance, "RFilled (no slippage)");
        trade.SlippageR.ShouldBe(0.0, Tolerance, "SlippageR");
        trade.MaeR.ShouldBe(expected.Mae, Tolerance, "MaeR");
        trade.MfeR.ShouldBe(expected.Mfe, Tolerance, "MfeR");
        trade.SessionsHeld.ShouldBe(expected.Sessions, "SessionsHeld");
        outcome.StopGapExit.ShouldBe(expected.StopGap, "StopGapExit");
        outcome.StopLoosenIgnored.ShouldBe(expected.Ignored, "StopLoosenIgnored");
        outcome.TimeStopAtLastClose.ShouldBe(expected.TimeStopAtLastClose, "TimeStopAtLastClose");
        outcome.ToCounters().ExitsTimeStop.ShouldBe(expected.Reason == ExitReason.TimeStop ? 1 : 0);
        trade.TargetPrice.ShouldNotBeNull().ShouldBe(trade.EntryPrice + ((row.Side == Side.Buy ? 1 : -1) * row.Reward * Math.Abs(trade.EntryPrice - trade.StopPrice)), 1e-9);
        trade.StopPrice.ShouldBe(row.Side == Side.Buy ? 98.00 : 102.00);

        if (expected.Legs.Count == 0)
        {
            trade.Legs.ShouldBeNull();
            trade.ExitPrice.ShouldBe(expected.Price!.Value, Tolerance);
            trade.ExitEpochSeconds.ShouldBe(DayBuilder.Epoch(expected.Date ?? Monday, expected.At!));
            return;
        }

        var legs = trade.Legs.ShouldNotBeNull();
        legs.Select(l => (l.EpochSeconds, l.Price, l.FilledPrice, l.Share, l.Kind, l.Reason))
            .ShouldBe(expected.Legs.Select(l => (DayBuilder.Epoch(l.Date ?? Monday, l.At), l.Price, l.Price, l.Share, l.Kind, l.Reason)));
        legs.Where(l => l.Kind == LegKind.Exit).Sum(l => l.Share).ShouldBe(1m, "the shares out equal the shares in");
        trade.ExitPrice.ShouldBe(legs[^1].Price);
        trade.ExitEpochSeconds.ShouldBe(legs[^1].EpochSeconds);
        outcome.ToCounters().LegsTotal.ShouldBe(legs.Count);
    }

    [Fact]
    public void The_break_even_stop_and_T2_are_first_live_in_the_minute_after_T1()
    {
        // B17 and B18 above are the hand rows; here the whole of the property for the BE rows: T1 is never in the minute of the leg that follows it when that leg is a break-even stop or T2
        foreach (var row in Rows.Where(r => r.Policy.Partial == PartialKind.Be && r.Expected.Legs.Count > 0))
        {
            var after = row.Expected.Legs.SkipWhile(l => l.Reason != "T1").Skip(1).First();
            var t1 = row.Expected.Legs.First(l => l.Reason == "T1");
            if (after.Reason is "Stop" or "Target")
            {
                (after.Date ?? Monday, after.At).ShouldNotBe((t1.Date ?? Monday, t1.At), row.Name);
            }
        }
    }
}
