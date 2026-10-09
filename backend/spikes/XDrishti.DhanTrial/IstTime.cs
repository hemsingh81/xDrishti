using System.Globalization;

namespace XDrishti.DhanTrial;

/// <summary>Indian Standard Time helpers (fixed UTC+05:30, no daylight saving).</summary>
internal static class IstTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    private static readonly string[] Formats =
    [
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm",
    ];

    /// <summary>Parses a zone-less timestamp as Indian time; strings that carry their own offset are honoured.</summary>
    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset) && text.Contains('+', StringComparison.Ordinal))
        {
            value = withOffset;
            return true;
        }

        if (DateTime.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            value = new DateTimeOffset(local, Offset);
            return true;
        }

        return false;
    }

    public static DateTimeOffset ToIst(DateTimeOffset instant) => instant.ToOffset(Offset);
}
