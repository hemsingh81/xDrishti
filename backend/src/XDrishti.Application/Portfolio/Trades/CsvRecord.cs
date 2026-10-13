namespace XDrishti.Application.Portfolio.Trades;

/// <summary>One record of a CSV file: the line it starts on, its fields and how many characters it took.</summary>
internal sealed record CsvRecord(int Line, IReadOnlyList<string> Fields, int Length);
