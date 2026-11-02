using System.Text.RegularExpressions;

namespace XDrishti.Domain.Filings;

/// <summary>The file-name rules of the inbox (P15-01). A name carries no meaning: no date and no symbol is ever read from it.</summary>
public static partial class FilingNames
{
    public const string SidecarSuffix = ".meta.json";

    public const int MaxSidecarBytes = 4096;

    private static readonly string[] Extensions = [".xml", ".xbrl", ".csv", ".pdf", ".txt"];

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,119}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    public static bool IsSidecarName(string name) => name.EndsWith(SidecarSuffix, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidName(string name) => NamePattern().IsMatch(name);

    /// <summary>The extension (with its dot, lower case) when it is one of the accepted ones, else null.</summary>
    public static string? AcceptedExtension(string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return Array.IndexOf(Extensions, extension) >= 0 ? extension : null;
    }

    public static string SidecarName(string fileName) => fileName + SidecarSuffix;
}
