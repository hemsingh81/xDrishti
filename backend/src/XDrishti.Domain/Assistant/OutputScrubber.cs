using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant;

/// <summary>
/// Replaces what looks like a secret or an identity number in FREE TEXT with <see cref="Marker"/> and counts it (spec P9-02 AC 7, P9-00 D11): a PAN-shaped string, a JWT-shaped string, 32 or more base64 characters, a run of 9 or more digits. Applied only to cells the manifest marks
/// as text; ids, hashes, run keys and amounts never pass through here. Idempotent: the marker matches none of the patterns.
/// </summary>
public static partial class OutputScrubber
{
    public const string Marker = "[redacted]";

    /// <summary>The text with every match replaced; <paramref name="redactions"/> is the number of replacements.</summary>
    public static string Scrub(string text, out int redactions)
    {
        ArgumentNullException.ThrowIfNull(text);
        var count = 0;
        string Replace(Regex pattern, string input) => pattern.Replace(input, _ =>
        {
            count++;
            return Marker;
        });

        var result = Replace(Jwt(), text);
        result = Replace(Pan(), result);
        result = Replace(Base64(), result);
        result = Replace(Digits(), result);
        redactions = count;
        return result;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z]{5}[0-9]{4}[A-Z](?![A-Za-z0-9])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pan();

    [GeneratedRegex(@"(?=[A-Za-z0-9+/_=-]*[A-Za-z0-9])[A-Za-z0-9+/_=-]{32,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Base64();

    [GeneratedRegex(@"[0-9]{9,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Digits();
}
