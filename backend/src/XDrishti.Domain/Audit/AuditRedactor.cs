using System.Text.RegularExpressions;

namespace XDrishti.Domain.Audit;

/// <summary>
/// A second line behind the stores' own rule that no audit text holds a secret (spec P8-22 decision 6): a deterministic pattern pass over every text the screen shows or searches, so a writer's mistake or a secret typed into a reason cannot reach a screen.
/// It leans to masking: a false mask hides a harmless word, a false negative shows a secret. It still cannot find every shape (a secret in plain words), and says so on screen. It never hides a row, only the matching text. Idempotent. A pattern that
/// runs out of time fails closed: the whole text is masked. Pure.
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

        try
        {
            var result = text;
            foreach (var pattern in Patterns())
            {
                result = pattern.Replace(result, Mask);
            }

            return (result, !string.Equals(result, text, StringComparison.Ordinal));
        }
        catch (RegexMatchTimeoutException)
        {
            return (Mask, true);
        }
    }

    private static Regex[] Patterns() => [Authorization(), UrlCredentials(), ConnectionString(), KeyValue(), PasswordIs(), BareSecret(), Jwt(), Bearer(), Basic(), PasswordHash(), RecoveryCode(), Hex(), LongToken(), OneTimeCode()];

    // "Authorization: Bearer abc" / "Authorization: Basic dXNl..." : the scheme and the credential go together (first, before the key rule would stop at the scheme).
    [GeneratedRegex(@"authorization\s*[:=]\s*(?:(?:bearer|basic|digest|token)\s+)?\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Authorization();

    // scheme://user:pass@host : the user and password part.
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.-]*://[^\s/:@]*:[^\s/@]*@", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex UrlCredentials();

    // Server=..;Username=..;Password=.. : every part of a connection string that names a host, user or secret.
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:username|user\s?id|uid|server|host|data\s?source|database|password|pwd)\s*=\s*[^;]*;?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ConnectionString();

    // A secret-like name, with any prefix or suffix (my_token, sessionid), then ":" or "=" (quoted or JSON forms too) and the value.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])[A-Za-z0-9_.-]*(?:password|passwd|passphrase|pwd|pw|secret|token|api[\s_-]?key|private[\s_-]?key|access[\s_-]?key|recovery[\s_-]?code|refresh|cookie|credential|session|sid|client[\s_-]?id|authorization|totp|otp|auth)[A-Za-z0-9_.-]*[""']?\s*[:=]\s*[""']?\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex KeyValue();

    // "password is x", "secret was x".
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:password|passwd|passphrase|pwd|pin|secret|token)\s+(?:is|was)\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex PasswordIs();

    // "token abc", "secret x": the word that follows, unless it is plain prose around the word ("password for the account", "token expired").
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?:password|passwd|passphrase|token|secret)\s+(?!(?:for|of|to|in|on|at|by|and|or|the|a|an|has|had|have|was|were|are|is|will|must|should|can|not|no|too|expired|expires|changed|change|reset|updated|update|rotated|replaced|stored|set|check|checked|length|policy|rules?|refused|failed|accepted|saved|matches|mismatch|did|does|from|with|when|after|before)\b)[A-Za-z0-9_\-+/=.]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex BareSecret();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.?[A-Za-z0-9_-]*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\bBearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\bBasic\s+[A-Za-z0-9+/_-]{8,}={0,2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Basic();

    // argon2, bcrypt, scrypt and pbkdf2 hash strings.
    [GeneratedRegex(@"\$(?:argon2[a-z]*|2[abxy]|scrypt|pbkdf2[a-z0-9-]*)\$\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex PasswordHash();

    // abcd-efgh-ijkl : a recovery code is a whole chain of 4 or 5 character groups (a uuid has longer groups, so it does not match).
    [GeneratedRegex(@"(?<![A-Za-z0-9-])[A-Za-z0-9]{4,5}(?:-[A-Za-z0-9]{4,5}){2,}(?![A-Za-z0-9-])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex RecoveryCode();

    // A hex string of 32 or more digits (a token or a hash).
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Fa-f0-9]{32,}(?![A-Za-z0-9])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Hex();

    // A base64 or url-safe run of 40 or more characters.
    [GeneratedRegex(@"(?<![A-Za-z0-9+/=_-])[A-Za-z0-9+/_-]{40,}={0,2}(?![A-Za-z0-9+/=_-])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex LongToken();

    // A bare one-time code: six or eight digits standing alone (not part of a longer number, a decimal or a date).
    [GeneratedRegex(@"(?<![A-Za-z0-9.,:/-])(?:\d{6}|\d{8})(?![A-Za-z0-9-]|[.,]\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex OneTimeCode();
}
