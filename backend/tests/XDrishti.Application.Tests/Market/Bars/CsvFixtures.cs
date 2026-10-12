using System.Text;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>Generated 1-minute CSV text for the file-level checks: times only (the prices are the same valid ones in every row).</summary>
internal static class CsvFixtures
{
    public const string Header = "time,open,high,low,close,CE Breakout Level,PE Breakout Level,Volume";

    /// <summary>2024-04-01 09:15 India time as epoch seconds.</summary>
    public const long Day0 = 1_711_943_100;

    public const long Day = 86_400;

    /// <summary>
    /// The minutes of <paramref name="days"/> days from <see cref="Day0"/>, <paramref name="count"/> per day from the minute <paramref name="firstMinute"/> after 09:15
    /// (a regular session is 375 minutes from 0), each moved by <paramref name="shiftSeconds"/>.
    /// </summary>
    public static IEnumerable<long> Times(int days, int count = 375, int firstMinute = 0, long shiftSeconds = 0, long startDay = 0) =>
        Enumerable.Range(0, days).SelectMany(d => Enumerable.Range(firstMinute, count).Select(i => Day0 + ((startDay + d) * Day) + (i * 60L) + shiftSeconds));

    public static string Row(long time) => $"{time},100,101,99,100.5,,,10";

    public static string Csv(IEnumerable<long> times)
    {
        var text = new StringBuilder(Header).Append('\n');
        foreach (var time in times)
        {
            text.Append(Row(time)).Append('\n');
        }

        return text.ToString();
    }
}
