using System.Globalization;

namespace XDrishti.SimExport.Manifest;

/// <summary>
/// The name of the folder of a run (spec P3-04b1, E6): <c>&lt;strategy id lower-case, each '_' written '-'&gt;-&lt;from yyyyMMdd&gt;-&lt;to yyyyMMdd&gt;-&lt;first 12 hex of the run key&gt;</c>, for example
/// <c>vwap-pullback-20240401-20260430-3fa91c2d07be</c>. It is at most 80 characters over <c>a-z 0-9 -</c>, so it is safe as a folder name on any file system and as a path segment.
/// </summary>
public static class RunId
{
    /// <summary>The longest run id.</summary>
    public const int MaxLength = 80;

    private const int KeyChars = 12;

    /// <summary>The run id.</summary>
    /// <exception cref="ArgumentException">The strategy id is empty or has a character outside <c>A-Z a-z 0-9 _</c>, the run key is not 64 lower-case hex characters, or the result would be longer than <see cref="MaxLength"/>.</exception>
    public static string Of(string strategyId, DateOnly from, DateOnly to, string runKey)
    {
        ArgumentNullException.ThrowIfNull(strategyId);
        ExportText.RequireRunKey(runKey);
        if (strategyId.Length == 0 || strategyId.Any(c => c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')))
        {
            throw new ArgumentException("The strategy id must be made of letters, digits and underscores.", nameof(strategyId));
        }

        if (strategyId[0] == '_')
        {
            throw new ArgumentException("The strategy id must not start with an underscore: the run id would start with a hyphen.", nameof(strategyId));
        }

        var id = string.Create(
            CultureInfo.InvariantCulture,
            $"{strategyId.ToLowerInvariant().Replace('_', '-')}-{from:yyyyMMdd}-{to:yyyyMMdd}-{runKey[..KeyChars]}");
        return id.Length <= MaxLength ? id : throw new ArgumentException("The strategy id is too long: the run id would be " + id.Length + " characters, at most " + MaxLength + ".", nameof(strategyId));
    }
}
