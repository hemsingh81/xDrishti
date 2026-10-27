namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A column of an ad-hoc result: its name and the database type name.</summary>
public sealed record SqlColumn(string Name, string DbType);
