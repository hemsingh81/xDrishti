using System.Globalization;

namespace XDrishti.Infrastructure.Evidence;

/// <summary>Reads the <c>mem_limit</c> of one service from the text of a compose file, in MiB (<c>1g</c>, <c>512m</c>, <c>2048k</c>, a bare byte count). No YAML parser: the service block is the lines indented deeper than its name. Null when the service or the value is missing or unparseable.</summary>
internal static class ComposeMemLimit
{
    // shortcut: a line scan, not a YAML parser; a service written with anchors, merge keys (<<:) or a flow-style mapping reads as no limit (null, so UNAVAILABLE), never as a wrong one. Replace with a YAML package if compose.yaml ever needs them.
    public static long? Mib(string compose, string service)
    {
        ArgumentNullException.ThrowIfNull(compose);
        var inBlock = false;
        foreach (var raw in compose.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var indent = line.Length - trimmed.Length;
            if (indent == 2 && trimmed.EndsWith(':'))
            {
                inBlock = trimmed == service + ":";
                continue;
            }

            if (indent == 0)
            {
                inBlock = false;
            }

            if (inBlock && trimmed.StartsWith("mem_limit:", StringComparison.Ordinal))
            {
                var hash = trimmed.IndexOf(" #", StringComparison.Ordinal);
                var value = (hash < 0 ? trimmed : trimmed[..hash])["mem_limit:".Length..].Trim().Trim('"', '\'').ToLowerInvariant();
                return Parse(value);
            }
        }

        return null;
    }

    private static long? Parse(string value)
    {
        if (value.EndsWith('b') && value.Length > 1 && !char.IsDigit(value[^2]))
        {
            value = value[..^1]; // 512mb, 1gb
        }

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
        {
            return bytes >= 1024 * 1024 ? bytes / (1024 * 1024) : null;
        }

        var factor = value.Length > 1 ? value[^1] switch { 'g' => 1024d, 'm' => 1d, 'k' => 1d / 1024, _ => 0d } : 0d;
        return factor > 0 && double.TryParse(value[..^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) && n > 0 ? (long)(n * factor) : null;
    }
}
