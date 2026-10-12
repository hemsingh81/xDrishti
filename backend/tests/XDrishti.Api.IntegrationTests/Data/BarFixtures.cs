using System.Globalization;
using System.IO.Compression;
using System.Text;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>Generated 1-minute bars and CSV files for the data tests (nothing real is committed): deterministic, prices in whole hundredths.</summary>
internal static class BarFixtures
{
    public const string Header = "time,open,high,low,close,CE Breakout Level,PE Breakout Level,Volume";

    /// <summary>The start of the minute <paramref name="minuteOfDay"/> (India time) on the date, as epoch seconds.</summary>
    public static long Epoch(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds() + minuteOfDay * 60L;

    /// <summary>
    /// A regular session (09:15–15:29, 375 bars) or a part of one: <paramref name="count"/> bars from <paramref name="startMinute"/>. The walk is a function of
    /// <paramref name="seed"/> and the date, so the same call gives the same bars. <paramref name="skip"/> leaves out minutes (a gap).
    /// </summary>
    public static List<Bar> Session(DateOnly date, int seed, int startMinute = SessionClock.OpenMinute, int count = SessionClock.RegularBars, Func<int, bool>? skip = null)
    {
        var state = (uint)(seed * 7919 + date.DayNumber);
        uint Next() => state = (state * 1103515245u + 12345u) & 0x7fffffffu;

        var bars = new List<Bar>(count);
        var price = 10_000 + (long)(Next() % 5_000); // hundredths
        for (var i = 0; i < count; i++)
        {
            var minute = startMinute + i;
            var open = price;
            var close = Math.Max(100, open + (long)(Next() % 41) - 20);
            var high = Math.Max(open, close) + (long)(Next() % 15);
            var low = Math.Max(1, Math.Min(open, close) - (long)(Next() % 15));
            var volume = Next() % 11 == 0 ? 0 : (long)(Next() % 50_000);
            price = close;
            if (skip?.Invoke(minute) == true)
            {
                continue;
            }

            bars.Add(new Bar(Epoch(date, minute), open / 100.0, high / 100.0, low / 100.0, close / 100.0, volume));
        }

        return bars;
    }

    /// <summary>The Muhurat-style evening session: 60 bars from 18:15.</summary>
    public static List<Bar> EveningSession(DateOnly date, int seed) => Session(date, seed, startMinute: 18 * 60 + 15, count: 60);

    public static string Csv(IEnumerable<Bar> bars, params string[] extraLines)
    {
        var text = new StringBuilder(Header).Append('\n');
        foreach (var bar in bars)
        {
            text.Append(Line(bar)).Append('\n');
        }

        foreach (var line in extraLines)
        {
            text.Append(line).Append('\n');
        }

        return text.ToString();
    }

    public static string Line(Bar bar) => string.Create(
        CultureInfo.InvariantCulture,
        $"{bar.EpochSeconds},{bar.Open:R},{bar.High:R},{bar.Low:R},{bar.Close:R},,,{bar.Volume}");

    public static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    /// <summary>Writes <c>&lt;SYMBOL&gt;_1m.csv.gz</c> (or the plain <c>.csv</c>) into the folder and gives it a distinct modified time (one second apart per call).</summary>
    public static string WriteFile(string folder, string symbol, string csv, bool gzip = true, DateTimeOffset? modified = null)
    {
        var name = $"{symbol}_1m.csv" + (gzip ? ".gz" : string.Empty);
        var path = Path.Combine(folder, name);
        if (gzip)
        {
            File.WriteAllBytes(path, Gzip(csv));
        }
        else
        {
            File.WriteAllText(path, csv, new UTF8Encoding(false));
        }

        File.SetLastWriteTimeUtc(path, (modified ?? new DateTimeOffset(2026, 4, 30, 12, 0, 0, TimeSpan.Zero)).UtcDateTime);
        return name;
    }
}
