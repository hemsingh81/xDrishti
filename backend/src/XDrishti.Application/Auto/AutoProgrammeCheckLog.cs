using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Auto;

/// <summary>Structured log event of the AU programme check (AU-19; ids 7360 and 7361). A verdict word and a count only.</summary>
internal static partial class AutoProgrammeCheckLog
{
    [LoggerMessage(EventId = 7360, Level = LogLevel.Information, Message = "auto.programme_check.evaluated: {Verdict}, {Met} item(s) met")]
    public static partial void Evaluated(ILogger logger, string verdict, int met);

    [LoggerMessage(EventId = 7361, Level = LogLevel.Warning, Message = "auto.programme_check.jobs_unreadable: {Kind}")]
    public static partial void Unreadable(ILogger logger, string kind);
}
