namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// What <c>cost-sheet derive</c> found (spec CS-01): the proposed sheet text, the lines it could and could not derive, the residuals on the bills the rates came from and on the bills they did not, and the done-when verdict. It holds no bill number,
/// scrip name, client code, quantity or value: only dates, classes and rupee amounts.
/// </summary>
/// <param name="SheetId">The proposed id, <c>derived-yyyy-MM-dd</c>.</param>
/// <param name="Json">The sheet text, as <c>CostSheetJson.Parse</c> reads it; a line without bills is <c>null</c> in it (so it cannot be loaded as it is).</param>
/// <param name="ResidualsCsv">Every compared bill: date, class, set, billed total, modelled total, difference.</param>
/// <param name="Files">The report files read.</param>
/// <param name="RowsRead">The rows the reader kept.</param>
/// <param name="RowsRefused">The rows the reader refused.</param>
/// <param name="RowsRepeated">The rows repeated in or across files.</param>
/// <param name="Skipped">Rows that are not NSE cash equity, by reason.</param>
/// <param name="Unclassified">Rows that fit no class, or whose negative Oth. cell makes them an adjustment: not used.</param>
/// <param name="OneSidedWithBrokerage">One-sided NSE bills with brokerage above 0: not used, and the reason delivery brokerage is null when there are any.</param>
/// <param name="Assumed">One line for every value written that the bills cannot show (the owner checks each against the tariff).</param>
/// <param name="From">The first day of the bills the rates come from.</param>
/// <param name="To">The last day of the bills the rates come from.</param>
/// <param name="Lines">One entry per class and line of the bill, plus the shared GST line.</param>
/// <param name="DeriveSet">The older bills the rates come from.</param>
/// <param name="CheckSet">The newest bills, not used for the rates.</param>
/// <param name="Largest">The ten bills with the largest difference over both sets.</param>
public sealed record CostDerivation(
    string SheetId,
    string Json,
    string ResidualsCsv,
    int Files,
    int RowsRead,
    int RowsRefused,
    int RowsRepeated,
    IReadOnlyList<CostCheckCount> Skipped,
    int Unclassified,
    int OneSidedWithBrokerage,
    IReadOnlyList<string> Assumed,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DerivedLine> Lines,
    DerivedSet DeriveSet,
    DerivedSet CheckSet,
    IReadOnlyList<DerivedResidual> Largest);

/// <summary>One line of one class: the rate written to the sheet, or none (<c>unavailable</c>).</summary>
/// <param name="Class">The class (intraday equity, delivery equity) or <c>all</c> for GST.</param>
/// <param name="Line">The line (brokerage, gst, stt, sebi, stamp duty, txn charges, other).</param>
/// <param name="Rate">The median implied rate written, or null when no bill has the line above 0.</param>
/// <param name="Implied">The minimum, median, maximum and the bills they come from; null with no bill.</param>
/// <param name="Unstable">The rate moves by more than 5 % of its median over the bills: the owner decides on an effective date.</param>
/// <param name="From">The first day the rate moved (set only when unstable).</param>
/// <param name="To">The last day the rate moved (set only when unstable).</param>
/// <param name="Note">Why the rate is unavailable, when it is.</param>
public sealed record DerivedLine(string Class, string Line, decimal? Rate, CostCheckImplied? Implied, bool Unstable, DateOnly? From, DateOnly? To, string? Note = null);

/// <summary>How a set of bills fares against the proposed sheet.</summary>
/// <param name="Name">The set: derive or check.</param>
/// <param name="Bills">The bills in it.</param>
/// <param name="Within">The bills whose modelled total is within the bound of the billed total.</param>
/// <param name="Share">Within over bills (0 for an empty set, which never passes).</param>
public sealed record DerivedSet(string Name, int Bills, int Within, decimal Share)
{
    /// <summary>The done-when rule: at least 95 % of a non-empty set.</summary>
    public bool Passes => Bills > 0 && Share >= CostSheetDeriver.RequiredShare;
}

/// <summary>A bill: its date, class, set and the billed, modelled and difference (model minus bill) totals in rupees.</summary>
/// <param name="Date">The date.</param>
/// <param name="Class">The class.</param>
/// <param name="Set">derive or check.</param>
/// <param name="Billed">The billed total.</param>
/// <param name="Modelled">The total the proposed sheet gives.</param>
/// <param name="Difference">Modelled minus billed.</param>
public sealed record DerivedResidual(DateOnly Date, string Class, string Set, decimal Billed, decimal Modelled, decimal Difference);
