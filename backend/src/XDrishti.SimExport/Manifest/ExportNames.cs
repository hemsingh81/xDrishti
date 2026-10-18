namespace XDrishti.SimExport.Manifest;

/// <summary>
/// The rules for the name of a file of an export (spec P3-04b1, E6, E7): a relative name of one or two segments separated by one <c>/</c>, each segment non-empty, made of <c>A-Z a-z 0-9 . _ &amp; -</c>, neither <c>.</c> nor <c>..</c>, and not
/// long. The folder writer applies these and more (a device name, a case collision); a name that fails here can never reach a path.
/// </summary>
public static class ExportNames
{
    /// <summary>The longest segment of a name.</summary>
    public const int MaxSegmentLength = 100;

    /// <summary>The longest whole name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Whether the name satisfies the rules above.</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }

        var segments = 0;
        foreach (var segment in name.Split('/'))
        {
            segments++;
            if (segments > 2 || !IsValidSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A problem with the entries of a manifest as a sentence, or null: an invalid name, two names that differ only in case (one file on a case-insensitive disk), a hash that is not 64 lower-case hex characters, a negative length.</summary>
    internal static string? Problem(IReadOnlyList<ManifestEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!IsValid(entry.Name))
            {
                return "The file name is not valid: " + (entry.Name ?? "(null)") + ".";
            }

            if (!seen.Add(entry.Name))
            {
                return "The file name appears twice (names are compared ignoring case): " + entry.Name + ".";
            }

            if (!ExportText.IsLowerHex(entry.Sha256, ExportText.Sha256HexLength))
            {
                return "The hash of " + entry.Name + " is not 64 lower-case hexadecimal characters.";
            }

            if (entry.Bytes < 0)
            {
                return "The length of " + entry.Name + " is negative.";
            }
        }

        return null;
    }

    private static bool IsValidSegment(string segment)
    {
        if (segment.Length is 0 or > MaxSegmentLength || segment is "." or "..")
        {
            return false;
        }

        foreach (var c in segment)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '&' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
