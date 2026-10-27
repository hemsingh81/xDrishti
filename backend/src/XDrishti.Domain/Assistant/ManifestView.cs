namespace XDrishti.Domain.Assistant;

/// <summary>One view of the manifest. <paramref name="RowSource"/> is the base table whose planner estimate stands for the size of the view (for the catalog only).</summary>
public sealed record ManifestView(string Name, string Description, string Grain, ViewClass Class, string RowSource, IReadOnlyList<ManifestColumn> Columns);
