using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Filings;

/// <summary>Structured log events of the results parse step (event ids 7011 to 7014, P15-03): counts, ids and closed codes only. Never a value from a file, a path, a file name or an element text.</summary>
internal static partial class ResultsParseLog
{
    [LoggerMessage(EventId = 7011, Level = LogLevel.Information, Message = "filings.parsed filing_id={FilingId} version={Version} outcome={Outcome} facts={Facts}")]
    public static partial void Parsed(ILogger logger, Guid filingId, string version, string outcome, int facts);

    [LoggerMessage(EventId = 7012, Level = LogLevel.Warning, Message = "filings.parse_rejected filing_id={FilingId} version={Version} reason={Reason}")]
    public static partial void Rejected(ILogger logger, Guid filingId, string version, string reason);

    [LoggerMessage(EventId = 7013, Level = LogLevel.Information, Message = "filings.parse_flags filing_id={FilingId} flags={Flags}")]
    public static partial void Flags(ILogger logger, Guid filingId, string flags);

    [LoggerMessage(EventId = 7014, Level = LogLevel.Warning, Message = "filings.parse_failed filing_id={FilingId} error={Error}: the filing is left for the next run")]
    public static partial void Failed(ILogger logger, Guid filingId, string error);
}
