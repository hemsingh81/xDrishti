using System.Globalization;
using System.Text.Json;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Tolerant reading of Dhan's rows (ADR 0025): the field names come from Dhan's public documentation, not from a real answer of the owner's account, so a name matches in any case (<c>tradingSymbol</c>, <c>TradingSymbol</c>, <c>tradingsymbol</c>),
/// a number may be a JSON number or a string (with thousands separators), and a time may have one of several shapes. Anything that does not read is null - the caller decides what a missing value means. Pure.
/// </summary>
internal static class DhanJson
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm",
        "dd-MM-yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "MMM d, yyyy h:mm:ss tt", "MMM dd, yyyy HH:mm:ss",
    ];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "MMM d, yyyy", "MMM dd, yyyy", "MMMM d, yyyy", "dd-MM-yyyy", "dd/MM/yyyy", "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMM yyyy", "d MMM yyyy", "yyyy/MM/dd",
    ];

    /// <summary>The array of rows of an answer: the answer itself when it is an array, else the array under <c>data</c>; null when there is none.</summary>
    public static JsonElement? Rows(JsonElement body) => body.ValueKind switch
    {
        JsonValueKind.Array => body,
        JsonValueKind.Object when body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array => data,
        _ => null,
    };

    /// <summary>The first property of the names that holds a string or number, as trimmed text (names compared without regard to case); null when none does or it is blank or <c>null</c>.</summary>
    public static string? Text(JsonElement row, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            foreach (var property in row.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }

    /// <summary>A number from a JSON number or a string such as <c>"20,000.50"</c>; null when missing, not a number or beyond what a quantity or price can be (<paramref name="limit"/>).</summary>
    public static decimal? Number(JsonElement row, decimal limit, params string[] names)
    {
        var text = Text(row, names);
        return text is not null
            && decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            && Math.Abs(number) <= limit
            ? number
            : null;
    }

    /// <summary>A date and optional time in India time (Dhan sends no zone), from <c>2026-09-14 10:15:02</c>, <c>2026-09-14T10:15:02</c> or a date alone; null when it does not read.</summary>
    public static (DateOnly Date, TimeOnly? Time)? DateTime(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (System.DateTime.TryParseExact(value, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
        {
            return (DateOnly.FromDateTime(moment), TimeOnly.FromDateTime(moment));
        }

        // A zone (Z or an offset) means the instant is not India's wall clock: it is converted.
        if (value.Length > 19 && value[4] == '-' && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
        {
            var india = offset.ToOffset(TimeSpan.FromMinutes(330));
            return (DateOnly.FromDateTime(india.DateTime), TimeOnly.FromDateTime(india.DateTime));
        }

        return Date(value) is { } date ? (date, null) : null;
    }

    /// <summary>A date from the shapes Dhan's documents show (<c>2026-09-14</c>, <c>Sep 14, 2026</c>, <c>14/09/2026</c> …); a date with a time of day is cut to its date. Null when it does not read.</summary>
    public static DateOnly? Date(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // A date followed by a time of day ("2026-09-14 00:00:00"): the date alone counts.
        return value.Length > 10 && (value[10] is ' ' or 'T') && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) ? first : null;
    }
}
