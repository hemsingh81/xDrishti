using System.Text.RegularExpressions;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// A feature extension of <c>tf-1</c> (P13-05): its id (<c>md</c>, <c>ev</c>, <c>fu</c>, <c>llm</c>), its version (<c>md-1</c>) and the names of its columns in order. The columns follow the catalog values in a row. The version is part of every record and every id.
/// </summary>
/// <param name="Id">Two to eight lower-case letters.</param>
/// <param name="Version">The id, a dash and one to three digits.</param>
/// <param name="Names">The column names in order; may be empty (then B is A).</param>
public sealed partial record ExtensionGroup(string Id, string Version, IReadOnlyList<string> Names)
{
    /// <summary>The problem with the group, or null when it is usable.</summary>
    public string? Problem()
    {
        if (Id is null || !IdPattern().IsMatch(Id))
        {
            return "the extension id must be 2 to 8 lower-case letters";
        }

        if (Version is null || !VersionPattern().IsMatch(Version) || !Version.StartsWith(Id + "-", StringComparison.Ordinal))
        {
            return "the extension version must be the id, a dash and 1 to 3 digits";
        }

        if (Names is null || Names.Any(string.IsNullOrWhiteSpace) || Names.Distinct(StringComparer.Ordinal).Count() != Names.Count)
        {
            return "the extension names must be non-empty and distinct";
        }

        return Names.Any(n => TradeFeatureCatalog.IndexOf(n) >= 0) ? "an extension name may not repeat a name of the catalog" : null;
    }

    [GeneratedRegex("^[a-z]{2,8}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z]{2,8}-[0-9]{1,3}$")]
    private static partial Regex VersionPattern();
}
