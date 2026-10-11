using System.Globalization;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Reads the <c>expiryTime</c> Dhan sends with a token, as ISO text in India time unless it carries an offset. Anything else reads as
/// unknown (the caller then uses the default lifetime).
/// </summary>
internal static class DhanTime
{
    private static readonly string[] IsoLocalFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm",
    ];

    public static DateTimeOffset? Parse(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(value, IsoLocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoLocal))
        {
            return Sane(InIndia(isoLocal));
        }

        // ISO with an explicit offset or Z.
        return value.Length > 10 && value[4] == '-' && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset)
            && HasOffset(value)
            ? Sane(withOffset)
            : null;
    }

    private static DateTimeOffset InIndia(DateTime local) => new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), BrokerTokenExpiry.IndiaOffset);

    private static bool HasOffset(string value) =>
        value.EndsWith('Z') || value.EndsWith('z') || (value.Length > 6 && value[^6] is '+' or '-' && value[^3] == ':');

    private static DateTimeOffset? Sane(DateTimeOffset value) => value.Year is >= 2020 and <= 2100 ? value : null;
}
