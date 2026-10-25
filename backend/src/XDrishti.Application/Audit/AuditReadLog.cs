using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Audit;

/// <summary>Structured log events of the audit screen (spec P8-22; ids 4890 and 4891). Counts and flags only: never a filter value, the search text or a row.</summary>
internal static partial class AuditReadLog
{
    [LoggerMessage(EventId = 4890, Level = LogLevel.Information, Message = "audit.read {Rows} rows, {Unavailable} of 6 stores unavailable, filters {HasFilters}, search {HasText}, capped {Capped}")]
    public static partial void Read(ILogger logger, int rows, int unavailable, bool hasFilters, bool hasText, bool capped);

    [LoggerMessage(EventId = 4891, Level = LogLevel.Warning, Message = "audit.source_failed {Source}: {ExceptionType}")]
    public static partial void SourceFailed(ILogger logger, string source, string exceptionType);
}
