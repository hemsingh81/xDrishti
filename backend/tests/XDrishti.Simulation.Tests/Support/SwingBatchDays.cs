using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>One invented session: open, high, low, close (all on the 0.05 grid).</summary>
internal sealed record SwingRow(double O, double H, double L, double C);

/// <summary>
/// The fabricated daily series of the four stored swing strategies of P6-17b (ADR 0081). Session i is the weekday number i from Monday 2026-01-05 (<see cref="BaseDays.Date"/>, no holiday); a session is four minutes (09:15 open, 10:00 high,
/// 12:00 low, 15:29 close, volume 100 each) so that its daily bar is exactly the row. A ramp session opens at the close before it and has a wick of 0.25 each way. The decision of a daily strategy is at 15:30 of the trigger session and the fill is the
/// open of the next one. Every series ends on the session its test needs; the sessions after the trigger are the walk to the target.
/// </summary>
internal static class SwingBatchDays
{
    public static Bar[] Bars(IReadOnlyList<SwingRow> rows)
    {
        var bars = new List<Bar>();
        for (var i = 0; i < rows.Count; i++)
        {
            var (o, h, l, c) = (rows[i].O, rows[i].H, rows[i].L, rows[i].C);
            var date = BaseDays.Date(i);
            bars.Add(new Bar(Day.Epoch(date, Day.Minute("09:15")), o, o, o, o, 100));
            bars.Add(new Bar(Day.Epoch(date, Day.Minute("10:00")), o, h, o, h, 100));
            bars.Add(new Bar(Day.Epoch(date, Day.Minute("12:00")), h, h, l, l, 100));
            bars.Add(new Bar(Day.Epoch(date, Day.Minute("15:29")), l, c, l, c, 100));
        }

        return [.. bars];
    }

    /// <summary>Rows that open at the close before them (the first at <paramref name="firstOpen"/>) with a wick of 0.25.</summary>
    private static List<SwingRow> Chain(double firstOpen, IEnumerable<double> closes)
    {
        var rows = new List<SwingRow>();
        var previous = firstOpen;
        foreach (var c in closes)
        {
            rows.Add(new SwingRow(previous, Math.Max(previous, c) + 0.25, Math.Min(previous, c) - 0.25, c));
            previous = c;
        }

        return rows;
    }

    private static SwingRow R(double o, double h, double l, double c) => new(o, h, l, c);

    // ---- TREND_PULLBACK: BUY. 60 sessions climb 0.5 a session from 100.00 to 129.50: on a perfect ramp the EMA(20), seeded with the mean of the first 20, is the close less 9.5 steps (124.75 at session 59) and the SMA(50) the close less 24.5 steps
    // (117.25). Three sessions fall back (129.00, 128.00, 127.00; EMA(20) 125.15, 125.43, 125.58); session 63 opens 127.00, dips to 125.50 (at or under its EMA(20) 125.85) and closes 128.50 above it and above its open.
    // Session 61 wicks to 125.25 (a down session, so no entry), the lowest low of the three. Stop = lowest low of sessions 61 to 63 = 125.25 (a lookback of 2 would give 125.50); entry = open of session 64 = 128.50; risk 3.25; target 135.00, reached on session 65 (high 135.25).
    public static List<SwingRow> TrendPullbackRows(bool negative = false, bool lowAbove = false, bool closeUnderOpen = false, bool walk = true)
    {
        var rows = Chain(99.75, Enumerable.Range(0, 60).Select(k => 100 + 0.5 * k));
        rows.Add(R(129.5, 129.75, 128.75, 129.0));
        rows.Add(R(129.0, 129.25, 125.25, 128.0));
        rows.Add(R(128.0, 128.25, 126.75, 127.0));
        rows.Add(negative ? R(124.0, 125.2, 123.8, 125.0) : lowAbove ? R(127.0, 128.75, 126.0, 128.5) : closeUnderOpen ? R(129.0, 129.0, 125.5, 128.5) : R(127.0, 128.75, 125.5, 128.5));
        if (walk && !negative && !lowAbove && !closeUnderOpen)
        {
            rows.Add(R(128.5, 131.0, 128.25, 130.5));
            rows.Add(R(130.5, 135.25, 130.25, 135.0));
        }

        return rows;
    }

    /// <summary>TREND_PULLBACK, setup gate: 60 sessions fall 0.5 a session from 130.00 to 100.50 (EMA(20) 105.25 under SMA(50) 112.75), then session 60 dips to 100.00 and closes at 107.00 above the EMA(20) 105.42 and its open: the trigger holds, the trend does not.</summary>
    public static List<SwingRow> TrendPullbackDowntrendRows()
    {
        var rows = Chain(130.25, Enumerable.Range(0, 60).Select(k => 130 - 0.5 * k));
        rows.Add(R(101.0, 108.0, 100.0, 107.0));
        return rows;
    }

    // ---- NR7_ID_BO: BUY. Sessions 0 to 4 have ranges 2.0, 2.4, 2.2, 2.6, 2.1; session 5 is the mother (104.00 / 101.00, range 3.0); session 6 lies inside it (103.00 / 102.25, range 0.75, the narrowest of seven); session 7 closes at 103.90 above
    // the inside day's high 103.00. Stop = lowest low of sessions 6 and 7 = 102.25; entry = open of session 8 = 103.90; risk 1.65; target 107.20, reached on session 8 (high 107.50).
    private static readonly SwingRow[] Nr7Prefix =
    [
        R(100.0, 101.0, 99.0, 100.5), R(100.5, 101.7, 99.3, 101.0), R(101.0, 102.1, 99.9, 100.8), R(100.8, 102.4, 99.8, 101.5), R(101.5, 102.6, 100.5, 102.0), R(102.0, 104.0, 101.0, 103.0),
    ];

    public static List<SwingRow> Nr7Rows(bool negative = false, bool notInside = false, int narrowSession = -1, bool walk = true, bool tie = false)
    {
        var rows = new List<SwingRow>(Nr7Prefix);
        if (narrowSession >= 0)
        {
            rows[narrowSession] = tie ? R(100.5, 101.0, 100.25, 100.5) : R(100.5, 100.75, 100.25, 100.5); // a range of exactly 0.75 (binary-exact) ties the inside day's, 0.5 is narrower
        }

        if (notInside)
        {
            rows.Add(R(103.5, 104.1, 103.3, 103.6));
            rows.Add(R(103.6, 104.9, 103.5, 104.5));
            return rows;
        }

        rows.Add(R(102.5, 103.0, 102.25, 102.8));
        rows.Add(negative ? R(102.8, 103.0, 102.6, 103.0) : R(102.8, 104.2, 102.6, 103.9));
        if (walk && !negative && narrowSession < 0)
        {
            rows.Add(R(103.9, 107.5, 103.8, 107.2));
        }

        return rows;
    }

    /// <summary>NR7_ID_BO, the hold: the trigger series, then five sessions that stay between the stop 102.25 and the target 107.20; the position leaves at the close of its fifth session (session 12) at 104.00.</summary>
    public static List<SwingRow> Nr7HoldRows()
    {
        var rows = Nr7Rows(walk: false);
        for (var i = 0; i < 5; i++)
        {
            rows.Add(R(i == 0 ? 103.9 : 104.0, 104.5, 103.5, 104.0));
        }

        return rows;
    }

    // ---- RSI2_REVERSION: BUY. 210 sessions climb 0.2 a session from 100.00 to 141.80 (SMA(200) 122.10). Session 210 closes 1.00 lower (RSI(2): the average gain 0.2 / 2 = 0.1, the average loss 1.0 / 2 = 0.5, RSI 100 - 100 / 1.2 = 16.67, no entry),
    // session 211 closes 1.00 lower again (gain 0.05, loss 0.75, RSI 6.25 under 10) at 139.80 above the SMA(200) 122.28, with a wick down to 139.00. Session 209 wicks to 138.60, the lowest low of the three. Stop = lowest low of sessions 209 to 211 = 138.60 (a lookback of 2 would give 139.00); entry = open of session 212 = 139.80; risk 1.20;
    // target 142.20, reached on session 213 (high 142.50).
    public static List<SwingRow> Rsi2Rows(bool negative = false, bool walk = true)
    {
        var rows = Chain(99.8, Enumerable.Range(0, 210).Select(k => 100 + 0.2 * k));
        rows[209] = rows[209] with { L = 138.6 };
        rows.Add(R(141.8, 142.05, 140.55, 140.8));
        if (negative)
        {
            return rows;
        }

        rows.Add(R(140.8, 141.05, 139.0, 139.8));
        if (walk)
        {
            rows.Add(R(139.8, 141.0, 139.7, 140.9));
            rows.Add(R(140.9, 142.5, 140.8, 142.0));
        }

        return rows;
    }

    /// <summary>RSI2_REVERSION, setup gate: the same climb, then session 210 closes 1.00 lower (RSI 16.67) and session 211 falls 20.00 to 120.80 (RSI far under 10) under the SMA(200) 122.2: the trigger holds, the trend does not.</summary>
    public static List<SwingRow> Rsi2BelowAverageRows()
    {
        var rows = Chain(99.8, Enumerable.Range(0, 210).Select(k => 100 + 0.2 * k));
        rows[209] = rows[209] with { L = 138.6 };
        rows.Add(R(141.8, 142.05, 140.55, 140.8));
        rows.Add(R(140.8, 141.05, 120.55, 120.8));
        return rows;
    }

    // ---- HIGH_52W_MOMENTUM: BUY. 260 sessions of a base that alternates and drifts down: session k = 2m (even) closes 98.00 - 0.05 m, k = 2m + 1 closes 98.50 - 0.05 m, so the up sessions (odd k) fall steadily, none ties an earlier one, and the highest close of any 252 sessions
    // is 98.30 (session 9). Session 260 closes at 98.40, above all 252 closes of its window (and above the close before it, 92.05). Session 256 wicks to 90.80, the lowest low of the five. Stop = lowest low of sessions 256 to 260 = 90.80 (a lookback of 4 would give 91.30); entry = open of session 261 = 98.40; risk 7.60; target 113.60,
    // reached on session 261 (high 114.00); that session closes lower (98.30) so it is no second entry.
    public static double BaseClose(int k) => (k % 2 == 0 ? 98.0 : 98.5) - (0.05 * (k / 2));

    private static List<SwingRow> Base260(int spikeAt = -1)
    {
        var closes = Enumerable.Range(0, 260).Select(k => k == spikeAt ? 105.0 : BaseClose(k));
        return Chain(98.25, closes);
    }

    public static List<SwingRow> High52Rows(bool negative = false, bool walk = true)
    {
        var rows = Base260(negative ? 100 : -1);
        rows[256] = rows[256] with { L = 90.8 };
        rows.Add(R(92.05, 98.6, 91.8, 98.4));
        if (walk && !negative)
        {
            rows.Add(R(98.4, 114.0, 98.2, 98.3));
        }

        return rows;
    }

    /// <summary>HIGH_52W_MOMENTUM, trigger part: 261 sessions flat at 100.00. Every close equals the highest of its window (a tie counts) but none is above the one before it.</summary>
    public static List<SwingRow> High52FlatRows() => [.. Enumerable.Range(0, 261).Select(_ => R(100.0, 100.0, 100.0, 100.0))];
}
