namespace XDrishti.SimExport;

/// <summary>Checks on the small texts the export carries (run key, hash, format): each throws when the text could not be what the file says it is.</summary>
internal static class ExportText
{
    public const int Sha256HexLength = 64;

    /// <summary>The run key is the lower-case hex SHA-256 of the key text: 64 characters of <c>0-9a-f</c>.</summary>
    public static void RequireRunKey(string? runKey, string paramName = "runKey")
    {
        if (!IsLowerHex(runKey, Sha256HexLength))
        {
            throw new ArgumentException("The run key must be 64 lower-case hexadecimal characters.", paramName);
        }
    }

    public static bool IsLowerHex(string? text, int length)
    {
        if (text is null || text.Length != length)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
