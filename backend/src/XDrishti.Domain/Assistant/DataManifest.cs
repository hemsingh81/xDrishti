using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant;

/// <summary>
/// The manifest of what the model may read (spec P9-02): every view of schema <c>aiv</c>, every column with its class, kind, unit and description. One source for <c>describe_table</c>, the cell cleaning, the classification tests and the docs. It is code and is checked against
/// the live database both ways (an integration test), so a view or column without an entry, or an entry without a column, fails the build.
/// </summary>
public static partial class DataManifest
{
    /// <summary>Names a column of a view must never have (secrets, identity, accounts). A test checks every manifest column and every live column against it.</summary>
    public const string ForbiddenColumnPattern = "secret|token|password|passwd|client_?id|account_?(no|number)|pan|phone|email|dp_?id|demat|address";

    private static readonly Dictionary<string, ManifestView> ByName = ManifestViews.All.ToDictionary(v => v.Name, StringComparer.Ordinal);

    private static readonly Dictionary<string, ColumnKind> KindByColumn = BuildKinds();

    public static IReadOnlyList<ManifestView> Views => ManifestViews.All;

    /// <summary>The view of this name, or null.</summary>
    public static ManifestView? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>
    /// The kind of cells delivered under this output column name by <c>run_sql</c>, where the query's columns can be aliases: a name that is Text in any view is Text; a name known only as another kind has that kind; an unknown name (an alias, an aggregate) is Text,
    /// so that the cleaning errs on the side of caution.
    /// </summary>
    public static ColumnKind KindOfOutput(string columnName) => KindByColumn.GetValueOrDefault(columnName, ColumnKind.Text);

    public static bool IsForbiddenColumnName(string name) => ForbiddenName().IsMatch(name);

    [GeneratedRegex(ForbiddenColumnPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ForbiddenName();

    private static Dictionary<string, ColumnKind> BuildKinds()
    {
        var kinds = new Dictionary<string, ColumnKind>(StringComparer.Ordinal);
        foreach (var column in ManifestViews.All.SelectMany(v => v.Columns))
        {
            kinds[column.Name] = kinds.TryGetValue(column.Name, out var known) && known == ColumnKind.Text ? ColumnKind.Text : column.Kind;
        }

        return kinds;
    }
}
