namespace XDrishti.Api.CostSheets;

/// <summary>
/// The log lines of the <c>cost-sheet</c> verb (spec P3-03f, F4; event ids 4412 to 4414, the next free ones after the simulate verb's 4405 to 4411; unique across the solution, asserted by the architecture tests). The sheet id, its rates hash,
/// statuses and counts only: never a rate, a bill, a name, a client code or a path.
/// </summary>
internal static partial class CostSheetLog
{
    [LoggerMessage(EventId = 4412, Level = LogLevel.Information, Message = "Cost sheet {SheetId} loaded: {Status}, rates {RatesSha256}")]
    public static partial void Loaded(ILogger logger, string sheetId, string status, string ratesSha256);

    [LoggerMessage(EventId = 4413, Level = LogLevel.Information, Message = "Cost sheet {SheetId} shown, rates {RatesSha256}")]
    public static partial void Shown(ILogger logger, string sheetId, string ratesSha256);

    [LoggerMessage(EventId = 4414, Level = LogLevel.Information, Message = "Cost sheet {SheetId} checked, rates {RatesSha256}: {Files} files, {Compared} rows compared, {Beyond} beyond the tolerance")]
    public static partial void Checked(ILogger logger, string sheetId, string ratesSha256, int files, int compared, int beyond);
}
