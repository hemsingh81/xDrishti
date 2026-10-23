using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The fabricated days of the eight stored intraday strategies of P6-17a (ADR 0079). Every list is a prefix followed by the fixture day (the Monday after it). The trend prefix is <see cref="BandedDays.Up(DateOnly, int)"/>:
/// 21 banded sessions ending at 110.50, so the previous day has high 111.00, low 110.00, close 110.50, the daily average is below the close and the hourly and 15-minute averages rise. The range prefix oscillates around 110 so
/// that the ADX stays low; the drifting prefix rises 0.12 a bar so that it stays high. A row <c>HH:MM o/h/l/c</c> sets one minute; the minutes that follow repeat its close, so a row at the first minute of a bar is the bar.
/// SELL days are the mirror of the BUY days about 110.50. The "setup" days at the end have the trigger of the positive day and a setup gate that is false, so that deleting the gate makes a candidate appear.
/// </summary>
internal static class IntradayBatchDays
{
    public static readonly DateOnly Date = Day.Monday;

    private static Day Fresh() => Day.Dense(Date).Start(BandedDays.Pivot);

    private static Bar[] OnTrend(Day day, bool down = false) => [.. down ? BandedDays.Down(Date) : BandedDays.Up(Date), .. day.Bars()];

    private static string Row(string clock, double o, double h, double l, double c) =>
        string.Create(CultureInfo.InvariantCulture, $"{clock} {o:0.00}/{h:0.00}/{l:0.00}/{c:0.00}");

    /// <summary>Bar k of an oscillating or drifting session: wicks of 0.05 to 0.15 that vary with k, so that the directional movement of both directions is real (a perfectly regular wick gives an ADX of 100).</summary>
    private static string Wicked(double previous, double close, int k) =>
        string.Create(CultureInfo.InvariantCulture, $"{previous:0.00}/{Math.Max(previous, close) + 0.05 * (1 + k % 3):0.00}/{Math.Min(previous, close) - 0.05 * (1 + (k + 1) % 3):0.00}/{close:0.00}");

    /// <summary>A session of 25 bars that oscillates: bar k closes at 110.30 (even k) or 109.70 (odd k); a row in <paramref name="replace"/> replaces the bar with that clock time.</summary>
    private static Day Range(DateOnly date, Dictionary<string, string>? replace = null)
    {
        var day = Day.Dense(date).Start(110.0);
        var previous = 110.0;
        for (var k = 0; k < 25; k++)
        {
            var clock = Day.Clock(9 * 60 + 15 + 15 * k);
            var close = k % 2 == 0 ? 110.30 : 109.70;
            string? custom = null;
            var replaced = replace?.TryGetValue(clock, out custom) ?? false;
            var row = replaced ? custom! : Wicked(previous, close, k);
            day.Set(clock + " " + row);
            previous = replaced ? double.Parse(row.Split('/')[3], CultureInfo.InvariantCulture) : close;
        }

        return day;
    }

    /// <summary>Three sessions that drift up 0.12 a bar from 101.00 to 110.00: a strong trend, so the ADX is high at the start of the next day.</summary>
    private static Day[] Drift()
    {
        var days = new List<Day>();
        var level = 101.0;
        for (var s = 0; s < 3; s++)
        {
            var day = Day.Dense(Date.AddDays(s - 3)).Start(level);
            for (var k = 0; k < 25; k++)
            {
                var close = level + 0.12;
                day.Set(Day.Clock(9 * 60 + 15 + 15 * k) + " " + Wicked(level, close, k));
                level = close;
            }

            days.Add(day);
        }

        return [.. days];
    }

    private static Bar[] OnRange(Day today, bool drifting = false) =>
        Day.Join(drifting ? [.. Drift(), today] : [Range(Date.AddDays(-3)), Range(Date.AddDays(-2)), Range(Date.AddDays(-1)), today]);

    // ---- PDH_PDL_BREAK: BUY. Bar 4 (10:00) closes at 111.25 above the previous high 111.00 while bar 3 closed at 110.75; stop = lowest low of bars 2 to 4 = 110.25; entry 111.25; risk 1.00; target 113.25.
    // The negative day closes bar 3 on the level (111.00), which is not above it.
    public static Bar[] PdhBreak(bool negative = false)
    {
        var day = Fresh().Set("09:15 110.50/110.75/110.25/110.50", "09:30 110.50/110.75/110.25/110.50");
        return OnTrend(negative
            ? day.Set("09:45 110.50/111.50/110.50/111.00")
            : day.Set("09:45 110.50/111.00/110.50/110.75", "10:00 110.75/111.50/110.75/111.25", "10:20 111.25/113.50/111.25/113.25"));
    }

    // ---- PDH_PDL_REJECT: BUY. Bar 3 (09:45) wicks to 109.75 below the previous low 110.00 and closes at 110.40 above it and above its open 110.25; stop = that low; entry 110.40; risk 0.65; target 111.70.
    public static Bar[] PdlReject(bool negative = false)
    {
        var day = Fresh().Set("09:15 110.50/110.60/110.50/110.55", "09:30 110.55/110.55/110.50/110.50");
        return OnTrend(negative
            ? day.Set("09:45 110.25/110.50/109.75/109.90")
            : day.Set("09:45 110.25/110.50/109.75/110.40", "10:05 110.40/111.75/110.40/111.70"));
    }

    // ---- VWAP_RECLAIM: BUY. Bars 1 and 2 close below the session VWAP (109.9167, 109.7083), bar 3 closes at 110.00 above VWAP 109.7778 and above its open; stop = lowest low of bars 1 to 3 = 109.25; entry 110.00;
    // risk 0.75; target 111.50. With a falling daily prefix (daily close 110.50 below its EMA(20) 115.25) the same day has a false setup.
    public static Bar[] VwapReclaim(bool negative = false, bool fallingPrefix = false)
    {
        var day = Fresh().Set("09:15 110.50/110.50/109.50/109.75", "09:30 109.75/109.75/109.25/109.50");
        return OnTrend(negative
            ? day.Set("09:45 109.50/109.75/109.25/109.50")
            : day.Set("09:45 109.50/110.25/109.50/110.00", "10:05 110.00/111.60/110.00/111.50"), fallingPrefix);
    }

    // ---- EMA_PULLBACK: BUY. In the rising prefix the 15-minute EMA(9) stays above EMA(21) and the last hourly close above the hourly EMA(21). By hand (see the arithmetic test): after bar 3 EMA(21) is about 110.48
    // and EMA(9) about 110.55; bar 3 (09:45) dips to 110.20 (0.28 below the EMA(21)) and closes at 110.70 (0.15 above the EMA(9)) above its open 110.55; stop = that low; entry 110.70; risk 0.50; target 111.70.
    public static Bar[] EmaPullback(bool negative = false)
    {
        var day = Fresh().Set("09:15 110.50/110.60/110.50/110.55", "09:30 110.55/110.55/110.55/110.55");
        return OnTrend(negative
            ? day.Set("09:45 110.55/110.70/110.55/110.65")
            : day.Set("09:45 110.55/110.75/110.20/110.70", "10:05 110.70/111.75/110.70/111.70"));
    }

    // ---- GAP_GO: BUY. The day opens at 111.25 (+0.68 % on 110.50). The opening range is bar 1 (high 111.75, low 111.00); bar 3 closes at 112.10 above the range high after a close of 111.60 inside it; stop = range low 111.00;
    // entry 112.10; risk 1.10; target 114.30. The negative day is the same day 0.50 lower: the gap is 0.23 %, below the 0.5 % minimum, and the trigger is the same.
    public static Bar[] GapGo(bool negative = false)
    {
        var d = negative ? -0.5 : 0.0;
        return OnTrend(Day.Dense(Date).Start(BandedDays.Pivot + 0.75 + d).Set(
            Row("09:15", 111.25 + d, 111.75 + d, 111.00 + d, 111.50 + d), Row("09:30", 111.50 + d, 111.75 + d, 111.25 + d, 111.60 + d),
            Row("09:45", 111.60 + d, 112.25 + d, 111.60 + d, 112.10 + d), Row("10:05", 112.10 + d, 114.40 + d, 112.10 + d, 114.30 + d)));
    }

    // ---- GAP_FILL: BUY. The day opens at 109.75 (-0.68 %), inside the 0.5 % to 3 % band; bar 3 closes at 110.00 above the day's open after a close at 109.50 at or below it; stop = lowest low of bars 1 to 3 = 109.25;
    // entry 110.00; risk 0.75; target 111.50. The negative day is the same day 3.00 lower: the gap is -3.4 %, beyond the band, and the trigger is the same.
    public static Bar[] GapFill(bool negative = false)
    {
        var d = negative ? -3.0 : 0.0;
        return OnTrend(Day.Dense(Date).Start(BandedDays.Pivot - 0.75 + d).Set(
            Row("09:15", 109.75 + d, 109.75 + d, 109.25 + d, 109.50 + d), Row("09:30", 109.50 + d, 109.75 + d, 109.25 + d, 109.50 + d),
            Row("09:45", 109.50 + d, 110.25 + d, 109.50 + d, 110.00 + d), Row("10:05", 110.00 + d, 111.60 + d, 110.00 + d, 111.50 + d)));
    }

    // ---- INSIDE_BAR_BO: BUY. Bar 1 is the mother (111.25 / 110.25), bar 2 lies inside it, bar 3 closes at 111.40 above the mother high; stop = lowest low of bars 1 to 3 = 110.25; entry 111.40; risk 1.15; target 113.70.
    // The negative day closes bar 3 at 111.20, inside the mother's range.
    public static Bar[] InsideBar(bool negative = false)
    {
        var day = Fresh().Set("09:15 110.50/111.25/110.25/110.75", "09:30 110.75/111.00/110.50/110.80");
        return OnTrend(negative
            ? day.Set("09:45 110.80/111.25/110.80/111.20")
            : day.Set("09:45 110.80/111.50/110.80/111.40", "10:05 111.40/113.80/111.40/113.70"));
    }

    // ---- MEAN_REV_BANDS: BUY. Three oscillating sessions, then a day whose bar 5 (10:15) drops to 108.90, below the lower Bollinger band, and bar 6 closes at 109.60 back above it and above its open, with a low ADX;
    // stop = lowest low of bars 4 to 6 = 108.80; entry 109.60; risk 0.80; target 110.80 (rr 1.5). The negative day oscillates all day and never leaves the band.
    public static Bar[] MeanRev(bool negative = false) => OnRange(negative
        ? Range(Date)
        : Range(Date, new Dictionary<string, string> { ["10:15"] = "109.70/109.70/108.80/108.90", ["10:30"] = "108.90/109.70/108.90/109.60" }).Set("10:50 109.60/110.90/109.60/110.80"));

    // ---- Setup-only negatives: the trigger of the positive day holds, one setup gate does not. ----

    /// <summary>VWAP_RECLAIM: the positive day after a falling daily prefix (the daily gate is false for BUY; mirrored, the rising prefix makes it false for SELL).</summary>
    public static Bar[] VwapReclaimFallingDaily() => VwapReclaim(fallingPrefix: true);

    /// <summary>EMA_PULLBACK, gate 0 (EMA(9) above EMA(21)): two falling bars before the trigger bar put EMA(9) below EMA(21) while the hourly gate still holds; bar 3 dips and closes above EMA(9) and its open.</summary>
    public static Bar[] EmaPullbackEmaGate() => OnTrend(Fresh().Set(
        "09:15 110.50/110.50/109.00/109.00", "09:30 109.00/109.00/108.50/108.50", "09:45 109.60/110.25/109.50/110.00"));

    /// <summary>EMA_PULLBACK, gate 1 (hourly close above hourly EMA(21)): the last session's 15:15 stub closes at 109.50, so the last closed hourly bar is below its EMA(21); the 15-minute averages are restored by two rising bars,
    /// then bar 3 dips to 110.40 and closes at 111.30 above EMA(9) and its open.</summary>
    public static Bar[] EmaPullbackHourlyGate()
    {
        var dates = BandedDays.SessionsBefore(Date, 21);
        var prefix = Day.Join([.. dates.Select((d, i) => BandedDays.Session(d, BandedDays.Pivot - 0.5 * (20 - i), i == 20 ? -1.0 : 0.0))]);
        var day = Fresh().Set("09:15 110.50/111.00/110.50/111.00", "09:30 111.00/111.20/111.00/111.20", "09:45 111.20/111.40/110.40/111.30");
        return [.. prefix, .. day.Bars()];
    }

    /// <summary>MEAN_REV_BANDS: three strongly drifting sessions (ADX high), then a dip far enough below the lower band and a reclaim; the ADX gate is false.</summary>
    public static Bar[] MeanRevTrending() => OnRange(
        Range(Date, new Dictionary<string, string> { ["10:15"] = "109.70/109.70/106.90/107.00", ["10:30"] = "107.00/108.70/107.00/108.60" }), drifting: true);
}
