namespace XDrishti.Domain.Assistant;

/// <summary>One column of a view of the manifest.</summary>
public sealed record ManifestColumn(string Name, ColumnKind Kind, DataClass Class, string Unit, string Description);
