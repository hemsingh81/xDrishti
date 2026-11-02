namespace XDrishti.Domain.Filings;

/// <summary>A row of a csv that is not loaded: its line number, a closed reason (<see cref="CalendarRejectReasons"/>) and, for a bad value, the column name. Never the cell's text.</summary>
public sealed record RowReject(int Line, string Reason, string? Column = null);
