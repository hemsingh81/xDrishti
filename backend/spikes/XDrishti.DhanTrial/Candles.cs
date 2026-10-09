using System.Globalization;
using System.Text.Json;

namespace XDrishti.DhanTrial;

/// <summary>One candle. Prices are doubles in this spike; the product keeps decimals (P1).</summary>
internal sealed record Bar(long Epoch, double Open, double High, double Low, double Close, long Volume);

internal static class CandleParser
{
    private static readonly string[] Fields = ["open", "high", "low", "close", "volume", "timestamp"];

    /// <summary>Parses Dhan's parallel-array response ({open:[], high:[], … timestamp:[]}). An empty or error body yields no bars.</summary>
    public static IReadOnlyList<Bar> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Fields.Any(f => !root.TryGetProperty(f, out var p) || p.ValueKind != JsonValueKind.Array))
        {
            return [];
        }

        var columns = Fields.Select(f => root.GetProperty(f).EnumerateArray().Select(e => e.GetDouble()).ToArray()).ToArray();
        var length = columns[0].Length;
        if (columns.Any(c => c.Length != length))
        {
            throw new FormatException("Candle arrays have different lengths.");
        }

        var bars = new Bar[length];
        for (var i = 0; i < length; i++)
        {
            bars[i] = new Bar((long)columns[5][i], columns[0][i], columns[1][i], columns[2][i], columns[3][i], (long)columns[4][i]);
        }

        return bars;
    }
}

internal readonly record struct DateRange(DateOnly From, DateOnly To);

internal static class DateChunker
{
    /// <summary>Splits [from, to] into consecutive ranges of at most <paramref name="maxDays"/> calendar days.</summary>
    public static IReadOnlyList<DateRange> Split(DateOnly from, DateOnly to, int maxDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDays, 1);
        var ranges = new List<DateRange>();
        for (var start = from; start <= to; start = start.AddDays(maxDays))
        {
            var end = start.AddDays(maxDays - 1);
            ranges.Add(new DateRange(start, end > to ? to : end));
        }

        return ranges;
    }

    public static string Intraday(DateOnly date, bool end) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (end ? " 15:30:00" : " 09:15:00");
}
