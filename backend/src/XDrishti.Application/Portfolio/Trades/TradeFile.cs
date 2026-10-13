namespace XDrishti.Application.Portfolio.Trades;

/// <summary>A row that cannot be imported: the line of the file it starts on and why.</summary>
public sealed record TradeBadRow(int Line, string Reason);

/// <summary>
/// What the reader made of a file: the data rows seen, the valid equity trades (without the rows repeated in the file), how many valid rows were repeats of a trade id already in the file, the rows skipped
/// (not equities, BSE) by reason, and the bad rows (the first <see cref="TradeCsvLimits.MaxListedBad"/> listed, all counted in <c>BadTotal</c>).
/// </summary>
internal sealed record TradeFile(
    int Rows,
    IReadOnlyList<ParsedTrade> Trades,
    int DuplicatesInFile,
    int Skipped,
    IReadOnlyDictionary<string, int> SkippedByReason,
    IReadOnlyList<TradeBadRow> Bad,
    int BadTotal)
{
    /// <summary>Valid rows, including the ones repeated in the file.</summary>
    public int Parsed => Trades.Count + DuplicatesInFile;
}
