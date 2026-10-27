using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A column of <c>describe_table</c>: the manifest's facts joined to the live database type.</summary>
public sealed record CatalogColumn(string Name, string DbType, ColumnKind Kind, DataClass Class, string Unit, string Description);
