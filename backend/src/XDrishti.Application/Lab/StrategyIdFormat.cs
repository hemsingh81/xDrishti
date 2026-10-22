using System.Text.RegularExpressions;

namespace XDrishti.Application.Lab;

/// <summary>The one rule for a strategy id: a letter, then up to 63 letters, digits or underscores. Shared by the store and the clone.</summary>
public static partial class StrategyIdFormat
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>Whether the text is a storable strategy id.</summary>
    public static bool IsValid(string? id) => id is not null && Pattern().IsMatch(id);
}
