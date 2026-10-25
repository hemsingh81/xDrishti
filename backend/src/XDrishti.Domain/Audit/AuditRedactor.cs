using System.Text.RegularExpressions;

namespace XDrishti.Domain.Audit;

/// <summary>
/// A second line behind the stores' own rule that no audit text holds a secret (spec P8-22 decision 6): a deterministic pattern pass over every text the screen shows or searches, so a writer's mistake or a secret typed into a reason cannot reach a screen. It cannot find every shape,
/// and says so on screen; it never hides a row, only the matching text. Idempotent. Pure.
/// </summary>
public static partial class AuditRedactor
{
    public const string Mask = "[redacted]";

    /// <summary>The text with every secret-shaped part replaced by <see cref="Mask"/>, and whether any was.</summary>
    public static (string Text, bool Redacted) Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, false);
        }

        var result = Secrets().Replace(text, Mask);
        return (result, !string.Equals(result, text, StringComparison.Ordinal));
    }

    // A JWT-shaped string; a bearer credential; "name=value" or "name: value" for a secret-like name (the value up to the next space); a long hex string (a token or a hash of 32 or more digits); a long unbroken token of 64 or more characters.
    [GeneratedRegex(
        @"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.?[A-Za-z0-9_-]*"
        + @"|\bBearer\s+[A-Za-z0-9._~+/=-]{8,}"
        + @"|\b(?:password|passwd|pwd|secret|token|api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?id|client[_-]?secret|authorization|totp|otp)\b\s*[:=]\s*\S+"
        + @"|(?<![A-Za-z0-9])[A-Fa-f0-9]{32,}(?![A-Za-z0-9])"
        + @"|(?<![A-Za-z0-9_+/=-])[A-Za-z0-9_+/=-]{64,}(?![A-Za-z0-9_+/=-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex Secrets();
}
