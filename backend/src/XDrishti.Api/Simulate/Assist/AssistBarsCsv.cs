using System.Globalization;
using System.Text;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>The cache file <c>second-source-bars.csv</c> and the vendor files of <c>--second-source-folder</c>. Plain comma-separated text, invariant culture, LF line ends; no quoting is needed or accepted.</summary>
internal static class AssistBarsCsv
{
    public const string CacheName = "second-source-bars.csv";

    public const long MaxBytes = 50L * 1024 * 1024;

    private const string CacheHeader = "source,symbol,date,epoch,open,high,low,close,volume";

    private static readonly string[] Columns = ["time", "open", "high", "low", "close"];

    private const int IndiaOffsetSeconds = 19800;

    private const long MillisecondsFrom = 100_000_000_000L;

    public sealed record CachedDay(string Source, IReadOnlyList<ReaderBar> Bars);

    public static string WriteCache(IReadOnlyDictionary<(string Symbol, DateOnly Date), CachedDay> days)
    {
        var text = new StringBuilder(CacheHeader).Append('\n');
        foreach (var ((symbol, date), day) in days.OrderBy(d => d.Key.Symbol, StringComparer.Ordinal).ThenBy(d => d.Key.Date))
        {
            foreach (var bar in day.Bars.OrderBy(b => b.EpochSeconds))
            {
                text.Append(day.Source).Append(',').Append(symbol).Append(',').Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                    .Append(bar.EpochSeconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(Number(bar.Open)).Append(',').Append(Number(bar.High)).Append(',').Append(Number(bar.Low)).Append(',').Append(Number(bar.Close)).Append(',').Append(Number(bar.Volume)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>Null when the text is not a cache file.</summary>
    public static Dictionary<(string Symbol, DateOnly Date), CachedDay>? ReadCache(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0] != CacheHeader)
        {
            return null;
        }

        var days = new Dictionary<(string, DateOnly), (string Source, List<ReaderBar> Bars)>();
        foreach (var line in lines.Skip(1))
        {
            var c = line.Split(',');
            if (c.Length != 9 || !DateOnly.TryParseExact(c[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || !long.TryParse(c[3], NumberStyles.None, CultureInfo.InvariantCulture, out var epoch)
                || !Decimals(c.AsSpan(4), out var v))
            {
                return null;
            }

            if (!days.TryGetValue((c[1], date), out var day))
            {
                day = (c[0], []);
                days[(c[1], date)] = day;
            }

            day.Bars.Add(new ReaderBar(epoch, v[0], v[1], v[2], v[3], v[4]));
        }

        return days.ToDictionary(d => d.Key, d => new CachedDay(d.Value.Source, d.Value.Bars));
    }

    /// <summary>
    /// The bars of one vendor file (for example a TradingView export): a header with <c>time, open, high, low, close</c> and optionally <c>volume</c> (any case); <c>time</c> is Unix seconds or milliseconds, or an ISO-8601 time (India time when it has no offset).
    /// A file with no volume column gives every bar volume 1 (a minute of the file is a minute that traded). Null when the text cannot be read.
    /// </summary>
    public static List<ReaderBar>? ParseVendor(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        var header = lines[0].Split(',', StringSplitOptions.TrimEntries).Select(h => h.ToLowerInvariant()).ToList();
        int[] at = [.. Columns.Select(h => header.IndexOf(h))];
        var volume = header.IndexOf("volume");
        if (at.Any(i => i < 0))
        {
            return null;
        }

        var bars = new List<ReaderBar>(lines.Length);
        foreach (var line in lines.Skip(1))
        {
            var c = line.Split(',', StringSplitOptions.TrimEntries);
            if (c.Length < header.Count || !Epoch(c[at[0]], out var epoch) || !Decimals([c[at[1]], c[at[2]], c[at[3]], c[at[4]], volume >= 0 ? c[volume] : "1"], out var v))
            {
                return null;
            }

            bars.Add(new ReaderBar(epoch, v[0], v[1], v[2], v[3], v[4]));
        }

        return [.. bars.OrderBy(b => b.EpochSeconds)];
    }

    /// <summary>The bars of the India date <paramref name="date"/> only, ascending; null when a minute is there twice.</summary>
    public static List<ReaderBar>? OfDate(IEnumerable<ReaderBar> bars, DateOnly date)
    {
        var day = bars.Where(b => DateOnly.FromDateTime(DateTime.UnixEpoch.AddSeconds(b.EpochSeconds + IndiaOffsetSeconds)) == date).OrderBy(b => b.EpochSeconds).ToList();
        return day.Select(b => b.EpochSeconds).Distinct().Count() == day.Count ? day : null;
    }

    private static bool Epoch(string text, out long epoch)
    {
        epoch = 0;
        if (text.Length > 0 && text.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out epoch))
            {
                return false;
            }

            epoch = epoch >= MillisecondsFrom ? epoch / 1000 : epoch;
            return true;
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return false;
        }

        // a time with no offset is India time
        var hasOffset = text.EndsWith('Z') || text.Contains('+', StringComparison.Ordinal) || text.LastIndexOf('-') > 9;
        epoch = hasOffset ? parsed.ToUnixTimeSeconds() : parsed.ToUnixTimeSeconds() - IndiaOffsetSeconds;
        return true;
    }

    private static bool Decimals(ReadOnlySpan<string> cells, out decimal[] values)
    {
        values = new decimal[5];
        for (var i = 0; i < 5; i++)
        {
            if (!decimal.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string Number(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
