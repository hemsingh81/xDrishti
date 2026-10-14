namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>A row that cannot be imported: the line of the file it starts on and why.</summary>
public sealed record DividendBadRow(int Line, string Reason);

/// <summary>A payment that appears more than once, identically, in the file: counted once. <c>Lines</c> are the lines of its occurrences (at most 10 listed).</summary>
public sealed record DividendRepeat(string Name, DateOnly PayDate, decimal Amount, int Occurrences, int FirstLine, IReadOnlyList<int> Lines);

/// <summary>
/// What the reader made of a dividend file: the data rows seen, the distinct valid rows in file order (identical rows counted once), how many extra rows were identical repeats (<c>Repeated</c>) and the first 200 payments repeated (<c>Repeats</c>), the rupees of
/// all valid rows (repeats included), and the bad rows (the first 200 listed, all counted in <c>BadTotal</c>).
/// </summary>
internal sealed record DividendFile(
    int Rows,
    IReadOnlyList<ParsedDividend> Items,
    int Repeated,
    IReadOnlyList<DividendRepeat> Repeats,
    decimal ParsedAmount,
    IReadOnlyList<DividendBadRow> Bad,
    int BadTotal)
{
    /// <summary>Valid rows, including the ones repeated in the file.</summary>
    public int Parsed => Items.Count + Repeated;
}
