using System.Globalization;

namespace XDrishti.DhanTrial;

internal enum EpochBase
{
    Unknown,
    TrueUtc,
    IstAsUtc,
}

internal enum BarLabel
{
    Unknown,
    Start,
    End,
}

internal sealed record TimestampSemantics(EpochBase Epoch, BarLabel Label, int DaysChecked, string Evidence)
{
    /// <summary>Minute-of-day (IST, bar-start labelled) of a candle, applying the detected semantics.</summary>
    public (DateOnly Date, int Minute) Locate(long epoch)
    {
        var shift = Epoch == EpochBase.IstAsUtc ? TimeSpan.Zero : IstTime.Offset;
        var local = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime + shift;
        var minute = (local.Hour * 60) + local.Minute - (Label == BarLabel.End ? 1 : 0);
        return (DateOnly.FromDateTime(local), minute);
    }
}

/// <summary>Works out what Dhan's epoch timestamps mean, from the shape of full sessions (09:15–15:30 IST).</summary>
internal static class TimestampAnalyzer
{
    private const int FullDayMinBars = 300;
    private const int Open = (9 * 60) + 15;
    private const int LastStart = (15 * 60) + 29;

    public static TimestampSemantics Classify(IReadOnlyList<Bar> bars)
    {
        TimestampSemantics best = new(EpochBase.Unknown, BarLabel.Unknown, 0, "No full session found.");
        var bestScore = 0.0;
        foreach (var (epochBase, shift) in new[] { (EpochBase.TrueUtc, IstTime.Offset), (EpochBase.IstAsUtc, TimeSpan.Zero) })
        {
            var days = bars
                .GroupBy(b => (DateTimeOffset.FromUnixTimeSeconds(b.Epoch).UtcDateTime + shift).Date)
                .Where(g => g.Count() >= FullDayMinBars)
                .Select(g =>
                {
                    var minutes = g.Select(b => ((DateTimeOffset.FromUnixTimeSeconds(b.Epoch).UtcDateTime + shift).Hour * 60) + (DateTimeOffset.FromUnixTimeSeconds(b.Epoch).UtcDateTime + shift).Minute).ToArray();
                    return (First: minutes.Min(), Last: minutes.Max());
                })
                .ToArray();
            if (days.Length == 0)
            {
                continue;
            }

            var start = days.Count(d => d.First == Open && d.Last == LastStart);
            var end = days.Count(d => d.First == Open + 1 && d.Last == LastStart + 1);
            var (label, hits) = start >= end ? (BarLabel.Start, start) : (BarLabel.End, end);
            var score = (double)hits / days.Length;
            if (score > bestScore)
            {
                bestScore = score;
                best = new TimestampSemantics(epochBase, label, days.Length, string.Create(CultureInfo.InvariantCulture, $"{hits} of {days.Length} full sessions look like epoch={epochBase}, bar label={label} (first/last minute {(label == BarLabel.Start ? "09:15/15:29" : "09:16/15:30")})."));
            }
        }

        return bestScore >= 0.8 ? best : best with { Epoch = EpochBase.Unknown, Label = BarLabel.Unknown };
    }

    /// <summary>Calendar date of a daily candle: midnight-IST stored as UTC epoch vs real UTC instant of IST midnight.</summary>
    public static DateOnly DailyDate(long epoch)
        => epoch % 86400 == 0
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime)
            : DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime + IstTime.Offset);
}

internal sealed record DayQuality(DateOnly Date, int Bars, int Expected, int MissingMinutes);

internal sealed record SymbolQuality(
    string Symbol,
    int TradingDays,
    IReadOnlyList<DateOnly> MissingDays,
    int TotalBars,
    int ExpectedBars,
    int MissingMinutes,
    int ZeroVolume,
    int Duplicates,
    int OutOfOrder,
    int OutOfSession,
    int BadPrice,
    IReadOnlyList<DayQuality> ShortDays)
{
    public double Completeness => ExpectedBars == 0 ? 0 : 100.0 * (ExpectedBars - MissingMinutes) / ExpectedBars;
}

internal static class BarAnalyzer
{
    public const int FullSession = 375;

    /// <summary>Per-day expected bar count: the best-covered series that day, capped at a full session (handles half-days and special sessions).</summary>
    public static IReadOnlyDictionary<DateOnly, int> ExpectedByDay(IEnumerable<IReadOnlyList<Bar>> series, TimestampSemantics semantics)
    {
        var expected = new Dictionary<DateOnly, int>();
        foreach (var bars in series)
        {
            foreach (var day in bars.Select(b => semantics.Locate(b.Epoch)).Where(l => InSession(l.Minute)).GroupBy(l => l.Date))
            {
                var count = Math.Min(FullSession, day.Select(l => l.Minute).Distinct().Count());
                expected[day.Key] = Math.Max(expected.GetValueOrDefault(day.Key), count);
            }
        }

        return expected;
    }

    /// <summary>Analyses one symbol against the days expected within its own requested window (<paramref name="from"/>..<paramref name="to"/>).</summary>
    public static SymbolQuality Analyze(string symbol, IReadOnlyList<Bar> bars, TimestampSemantics semantics, IReadOnlyDictionary<DateOnly, int> expectedByDay, DateOnly? from = null, DateOnly? to = null)
    {
        expectedByDay = expectedByDay.Where(kv => (from is null || kv.Key >= from) && (to is null || kv.Key <= to)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var located = bars.Select(b => (Bar: b, Where: semantics.Locate(b.Epoch))).ToArray();
        var outOfSession = located.Count(l => !InSession(l.Where.Minute));
        var inSession = located.Where(l => InSession(l.Where.Minute)).ToArray();
        var duplicates = inSession.GroupBy(l => (l.Where.Date, l.Where.Minute)).Sum(g => g.Count() - 1);
        var outOfOrder = 0;
        for (var i = 1; i < bars.Count; i++)
        {
            if (bars[i].Epoch < bars[i - 1].Epoch)
            {
                outOfOrder++;
            }
        }

        var zeroVolume = inSession.Count(l => l.Bar.Volume == 0);
        var badPrice = bars.Count(IsBadPrice);
        var byDay = inSession.GroupBy(l => l.Where.Date).ToDictionary(g => g.Key, g => g.Select(l => l.Where.Minute).Distinct().Count());
        var shortDays = new List<DayQuality>();
        var missingDays = new List<DateOnly>();
        var missingMinutes = 0;
        var expectedBars = 0;
        foreach (var (date, expected) in expectedByDay.OrderBy(kv => kv.Key))
        {
            expectedBars += expected;
            var actual = Math.Min(byDay.GetValueOrDefault(date), expected);
            if (actual == 0)
            {
                missingDays.Add(date);
            }

            if (actual < expected)
            {
                missingMinutes += expected - actual;
                shortDays.Add(new DayQuality(date, actual, expected, expected - actual));
            }
        }

        return new SymbolQuality(symbol, expectedByDay.Count, missingDays, bars.Count, expectedBars, missingMinutes, zeroVolume, duplicates, outOfOrder, outOfSession, badPrice, shortDays);
    }

    public static bool InSession(int minute) => minute is >= (9 * 60) + 15 and <= (15 * 60) + 29;

    private static bool IsBadPrice(Bar b)
        => b.Open <= 0 || b.High <= 0 || b.Low <= 0 || b.Close <= 0 || b.High < b.Low || b.Open > b.High || b.Open < b.Low || b.Close > b.High || b.Close < b.Low;
}

internal sealed record DailyComparison(int DaysCompared, int OpenMismatch, int HighMismatch, int LowMismatch, int CloseMismatch, int VolumeMismatch, double WorstVolumeRatio);

internal static class DailyCrossCheck
{
    /// <summary>Compares official daily candles with days derived from the 1-minute bars (same instrument, same dates only).</summary>
    public static DailyComparison Compare(IReadOnlyList<Bar> daily, IReadOnlyList<Bar> minutes, TimestampSemantics semantics)
    {
        var derived = minutes
            .Select(b => (Bar: b, Where: semantics.Locate(b.Epoch)))
            .Where(l => BarAnalyzer.InSession(l.Where.Minute))
            .GroupBy(l => l.Where.Date)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Where.Minute).Select(l => l.Bar).ToArray());
        int compared = 0, open = 0, high = 0, low = 0, close = 0, volume = 0;
        var worst = 1.0;
        foreach (var day in daily)
        {
            if (!derived.TryGetValue(TimestampAnalyzer.DailyDate(day.Epoch), out var bars))
            {
                continue;
            }

            compared++;
            open += Differs(day.Open, bars[0].Open) ? 1 : 0;
            high += Differs(day.High, bars.Max(b => b.High)) ? 1 : 0;
            low += Differs(day.Low, bars.Min(b => b.Low)) ? 1 : 0;
            close += Differs(day.Close, bars[^1].Close) ? 1 : 0;
            var sum = bars.Sum(b => b.Volume);
            if (day.Volume > 0 && sum > 0)
            {
                var ratio = (double)sum / day.Volume;
                volume += Math.Abs(ratio - 1) > 0.001 ? 1 : 0;
                if (Math.Abs(ratio - 1) > Math.Abs(worst - 1))
                {
                    worst = ratio;
                }
            }
        }

        return new DailyComparison(compared, open, high, low, close, volume, worst);
    }

    /// <summary>Daily closes that jump by more than −40% / +60% suggest an unadjusted split or bonus.</summary>
    public static IReadOnlyList<DateOnly> PriceDiscontinuities(IReadOnlyList<Bar> daily)
    {
        var found = new List<DateOnly>();
        var ordered = daily.OrderBy(b => b.Epoch).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            var change = (ordered[i].Close / ordered[i - 1].Close) - 1;
            if (change <= -0.4 || change >= 0.6)
            {
                found.Add(TimestampAnalyzer.DailyDate(ordered[i].Epoch));
            }
        }

        return found;
    }

    private static bool Differs(double a, double b) => Math.Abs(a - b) > Math.Max(0.011, 1e-4 * Math.Abs(b));
}
