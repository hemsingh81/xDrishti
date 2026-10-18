using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Research;

/// <summary>
/// The small pieces of the canonical text of a run: numbers in a form that does not depend on the culture, on the scale of a decimal or on the sign of a zero, and the SHA-256 of a text. The run key, the canonical JSON of a
/// strategy definition and the golden record of the tests all write numbers with these, so that a hash changes when a rule changes and never because a value was spelt differently (<c>-0</c> and <c>0</c> are one number;
/// <c>0.05</c> and <c>0.0500</c> are one tick).
/// </summary>
public static class CanonicalText
{
    /// <summary>The round-trip ("R") form with the invariant culture; negative zero is written <c>0</c>.</summary>
    public static string Number(double value) => value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The decimal with the fewest digits that keep its value (no trailing zeros, whatever its scale), invariant culture; zero is <c>0</c>.</summary>
    public static string Number(decimal value) => value == 0 ? "0" : value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>A whole number in the invariant culture.</summary>
    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The lower-case hex SHA-256 of the UTF-8 bytes of the text (no byte order mark).</summary>
    public static string Sha256Hex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
