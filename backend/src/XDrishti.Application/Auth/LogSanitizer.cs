using System.Globalization;
using System.Text;

namespace XDrishti.Application.Auth;

/// <summary>
/// Makes attacker-controlled text safe to write to a log: no control characters (log injection), a hard length limit,
/// and nothing that does not look like an e-mail address (the field may hold a mistyped password).
/// </summary>
internal static class LogSanitizer
{
    public const int MaxLength = 64;

    public const string NotAnEmail = "<not-an-email>";

    /// <summary>The cleaned, truncated value when it looks like an e-mail address (contains '@'); otherwise <see cref="NotAnEmail"/>.</summary>
    public static string Email(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NotAnEmail;
        }

        var cleaned = new StringBuilder(Math.Min(value.Length, MaxLength));
        foreach (var c in value)
        {
            if (IsLineOrControl(c))
            {
                continue;
            }

            cleaned.Append(c);
            if (cleaned.Length == MaxLength)
            {
                break;
            }
        }

        var result = cleaned.ToString().Trim();
        return result.Contains('@', StringComparison.Ordinal) ? result : NotAnEmail;
    }

    private static bool IsLineOrControl(char c) =>
        char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
