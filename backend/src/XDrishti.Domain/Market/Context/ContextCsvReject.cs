namespace XDrishti.Domain.Market.Context;

/// <summary>A refused row: a closed code and the field that failed. Never the content of the row.</summary>
public sealed record ContextCsvReject(int Line, string Code, string Field);
