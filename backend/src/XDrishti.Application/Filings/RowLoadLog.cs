using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Filings;

/// <summary>Structured log events of the calendar and shareholding loaders (event ids 6995 to 7000, P15-04): counts and closed reason codes only. Never a cell, a symbol, a file name or a path.</summary>
internal static partial class RowLoadLog
{
    [LoggerMessage(EventId = 6995, Level = LogLevel.Information, Message = "filings.rows_loaded kind={Kind} files={Files} loaded={Loaded} revisions={Revisions} out_of_order={OutOfOrder} duplicate={Duplicate} unavailable_time={Unavailable} sum_off={SumOff}")]
    public static partial void Loaded(ILogger logger, string kind, int files, int loaded, int revisions, int outOfOrder, int duplicate, int unavailable, int sumOff);

    [LoggerMessage(EventId = 6996, Level = LogLevel.Information, Message = "filings.rows_rejected kind={Kind} count={Count} reasons={Reasons}")]
    public static partial void Rejected(ILogger logger, string kind, int count, string reasons);

    [LoggerMessage(EventId = 6997, Level = LogLevel.Warning, Message = "filings.rows_failed kind={Kind} error={Error}: the file is left for the next run")]
    public static partial void Failed(ILogger logger, string kind, string error);
}
