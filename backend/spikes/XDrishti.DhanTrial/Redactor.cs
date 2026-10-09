using System.Text.RegularExpressions;

namespace XDrishti.DhanTrial;

/// <summary>Masks known secret values and secret-looking patterns (JWTs, token query parameters) in any text before it is shown or stored.</summary>
internal sealed partial class Redactor
{
    public const string Mask = "<redacted>";

    private readonly Lock _gate = new();
    private string[] _secrets = [];

    public Redactor(IEnumerable<string>? secrets = null)
    {
        foreach (var secret in secrets ?? [])
        {
            Add(secret);
        }
    }

    /// <summary>Registers a secret value (ignored when too short to be meaningful).</summary>
    public void Add(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 4)
        {
            return;
        }

        lock (_gate)
        {
            _secrets = [.. _secrets.Append(secret).Distinct(StringComparer.Ordinal).OrderByDescending(s => s.Length)];
        }
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string[] secrets;
        lock (_gate)
        {
            secrets = _secrets;
        }

        var result = text;
        foreach (var secret in secrets)
        {
            result = result.Replace(secret, Mask, StringComparison.Ordinal);
        }

        result = JwtPattern().Replace(result, Mask);
        return SecretQuery().Replace(result, "${key}" + Mask);
    }

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"(?<key>\b(?:pin|totp|tokenId|access[-_]?token|app_secret|app_id)=)[^&\s""]+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretQuery();
}
