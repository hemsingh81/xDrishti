using System.Globalization;
using System.Text;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The opaque cursors of the keyset reads (spec P3-10, D7 and criterion 18b): url-safe base64 of a tagged text. A cursor of the list of runs is <c>r1:&lt;created-at ticks&gt;:&lt;id&gt;</c>, a cursor of the trades of a run is
/// <c>t1:&lt;0|1&gt;:&lt;ordinal&gt;</c>. A cursor is parsed strictly and a foreign or damaged one is refused; it is only ever used as a bound parameter.
/// </summary>
internal static class PageCursor
{
    private const string RunTag = "r1:";

    private const string TradeTag = "t1:";

    private const int MaxLength = 80;

    public static string ForRun(DateTimeOffset createdAt, long id) => Encode(RunTag + createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + id.ToString(CultureInfo.InvariantCulture));

    public static string ForTrade(bool openAtEnd, int ordinal) => Encode(TradeTag + (openAtEnd ? "1" : "0") + ":" + ordinal.ToString(CultureInfo.InvariantCulture));

    public static bool TryReadRun(string cursor, out DateTimeOffset createdAt, out long id)
    {
        createdAt = default;
        id = 0;
        if (Decode(cursor, RunTag) is not { } parts || parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks > DateTimeOffset.MaxValue.Ticks
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    public static bool TryReadTrade(string cursor, out bool openAtEnd, out int ordinal)
    {
        openAtEnd = false;
        ordinal = 0;
        if (Decode(cursor, TradeTag) is not { } parts || parts.Length != 2 || parts[0] is not ("0" or "1")
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ordinal))
        {
            return false;
        }

        openAtEnd = parts[0] == "1";
        return true;
    }

    private static string Encode(string text) => Convert.ToBase64String(Encoding.ASCII.GetBytes(text)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string[]? Decode(string cursor, string tag)
    {
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxLength)
        {
            return null;
        }

        var padded = cursor.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        try
        {
            var text = Encoding.ASCII.GetString(Convert.FromBase64String(padded));
            return text.StartsWith(tag, StringComparison.Ordinal) ? text[tag.Length..].Split(':') : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
