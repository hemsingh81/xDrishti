namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// What the bill check found (spec P3-03f, F5): classes, counts, differences and implied rates. It holds no bill number, scrip name, client code, quantity or value: the original report files hold personal lines and bills, and the check prints
/// only what it can say without them.
/// </summary>
/// <param name="SheetId">The sheet that was checked.</param>
/// <param name="RatesSha256">Its rates hash.</param>
/// <param name="Files">The report files read.</param>
/// <param name="RowsRead">The data rows the reader kept, after the rows repeated across files were dropped.</param>
/// <param name="RowsRefused">The rows the reader refused (a bad date, quantity or charge); not compared.</param>
/// <param name="RowsRepeated">The rows repeated inside a file or across files (counted once).</param>
/// <param name="Classes">One entry per class that has rows: intraday equity and delivery.</param>
/// <param name="Skipped">Rows that are not NSE cash equity, by reason.</param>
/// <param name="Unclassified">Rows that fit no class (date and reason only), up to <see cref="CostBillCheck.MaxUnclassifiedListed"/>.</param>
/// <param name="UnclassifiedTotal">All rows that fit no class.</param>
/// <param name="AdjustmentRows">Rows with a negative Oth. cell: left out of the pass count.</param>
/// <param name="AdjustmentSum">The sum of those negative cells.</param>
/// <param name="UnmodelledOtherRows">Rows compared that have a positive Oth. cell while the sheet's <c>other_pct</c> is 0 (the cell is not in the comparison).</param>
/// <param name="Largest">The ten compared rows with the largest total difference (date, class and difference only).</param>
/// <param name="SuggestedChecked">A string for the sheet's <c>checked</c> field when every compared row is within the tolerance, else null. The command never writes it.</param>
public sealed record CostCheckReport(
    string SheetId,
    string RatesSha256,
    int Files,
    int RowsRead,
    int RowsRefused,
    int RowsRepeated,
    IReadOnlyList<CostCheckClass> Classes,
    IReadOnlyList<CostCheckCount> Skipped,
    IReadOnlyList<CostCheckUnclassified> Unclassified,
    int UnclassifiedTotal,
    int AdjustmentRows,
    decimal AdjustmentSum,
    int UnmodelledOtherRows,
    IReadOnlyList<CostCheckRow> Largest,
    string? SuggestedChecked);

/// <summary>The result for one class of bill row.</summary>
/// <param name="Name">The class: <c>intraday equity</c> or <c>delivery equity</c>.</param>
/// <param name="Rows">Rows compared.</param>
/// <param name="Passed">Rows whose total difference is within the tolerance.</param>
/// <param name="Beyond">Rows beyond it.</param>
/// <param name="LargestDifference">The largest absolute total difference of a row, in rupees.</param>
/// <param name="Lines">One entry per line of the bill.</param>
public sealed record CostCheckClass(string Name, int Rows, int Passed, int Beyond, decimal LargestDifference, IReadOnlyList<CostCheckLine> Lines);

/// <summary>One line of the bill (brokerage, gst, stt, sebi, stamp duty, txn charges, other) within a class.</summary>
/// <param name="Name">The line.</param>
/// <param name="LargestDifference">The largest absolute difference between the model and the bill, in rupees (not a pass or fail).</param>
/// <param name="Implied">The rates the bills imply, or null when no row has the line above 0.</param>
public sealed record CostCheckLine(string Name, decimal LargestDifference, CostCheckImplied? Implied);

/// <summary>The rate a line implies over the rows where it is above 0 (a proposal, never stored): a percent of the value, or rupees per crore for the SEBI fee, or a percent of the taxed lines for GST.</summary>
/// <param name="Minimum">The smallest.</param>
/// <param name="Median">The median (the mean of the two middle values for an even count).</param>
/// <param name="Maximum">The largest.</param>
/// <param name="Rows">The rows it is taken over.</param>
public sealed record CostCheckImplied(decimal Minimum, decimal Median, decimal Maximum, int Rows);

/// <summary>A count under a reason.</summary>
/// <param name="Reason">The reason.</param>
/// <param name="Rows">The number of rows.</param>
public sealed record CostCheckCount(string Reason, int Rows);

/// <summary>A row that fits no class: its date and why.</summary>
/// <param name="Date">The date of the row.</param>
/// <param name="Reason">Why the row fits no class.</param>
public sealed record CostCheckUnclassified(DateOnly Date, string Reason);

/// <summary>One of the rows with the largest difference: date, class and difference, nothing that identifies the bill.</summary>
/// <param name="Date">The date.</param>
/// <param name="Class">The class.</param>
/// <param name="Difference">The total difference (model minus bill) in rupees.</param>
public sealed record CostCheckRow(DateOnly Date, string Class, decimal Difference);
