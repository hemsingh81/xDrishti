using System.Text.RegularExpressions;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The screens of the free text of a compliance record (spec P11-05 criterion 3): the record never holds an address, a broker client id or a token. Each returns the reason in words, or null when the text is clean.
/// They run on <c>summary</c> and <c>isp</c> only. Pure.
/// </summary>
public static partial class ComplianceScreens
{
    [GeneratedRegex(@"(?<![0-9A-Za-z.])(?:\d{1,3}\.){3}\d{1,3}(?![0-9A-Za-z])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"(?<![0-9A-Za-z])\d{7,12}(?![0-9A-Za-z])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex ClientId();

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{24,}(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Token();

    [GeneratedRegex(@"[\s,;()\[\]<>""']+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Separators();

    /// <summary>Why the text must not be stored, or null: an IPv4 or IPv6 address, a client id (a whole word of 7 to 12 digits) or a token-like word (24 or more URL-safe characters).</summary>
    public static string? Reject(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Ipv4().IsMatch(text) || Separators().Split(text).Any(IsIpv6))
        {
            return "looks like an IP address";
        }

        if (ClientId().IsMatch(text))
        {
            return "looks like a client id";
        }

        return Token().IsMatch(text) ? "looks like a token" : null;
    }

    // Hex groups joined by colons with a "::" or all eight groups: a time such as 12:30:45 is not an address.
    private static bool IsIpv6(string word)
    {
        var w = word.TrimEnd('.');
        if (w.Length < 3 || w.Count(c => c == ':') < 2)
        {
            return false;
        }

        if (!w.All(c => Uri.IsHexDigit(c) || c is ':' or '.'))
        {
            return false;
        }

        return w.Contains("::", StringComparison.Ordinal) || w.Count(c => c == ':') >= 7;
    }
}
