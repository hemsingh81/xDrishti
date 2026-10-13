namespace XDrishti.Application.Portfolio;

/// <summary>Compares sort keys of rows: strings ignoring case, everything else by its natural order.</summary>
internal sealed class SortKeyComparer : IComparer<object?>
{
    public static SortKeyComparer Instance { get; } = new();

    public int Compare(object? x, object? y) =>
        x is string a && y is string b ? StringComparer.OrdinalIgnoreCase.Compare(a, b) : Comparer<object>.Default.Compare(x!, y!);
}
