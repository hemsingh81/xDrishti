using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio;

/// <summary>Structured log events of the holding groups. Only sizes and durations are logged — never figures, names or keys.</summary>
internal static partial class HoldingGroupsLog
{
    [LoggerMessage(EventId = 4041, Level = LogLevel.Information, Message = "Holding groups computed: by {By}, {Holdings} holdings, {Groups} groups in {ElapsedMs} ms")]
    public static partial void Computed(ILogger logger, string by, int holdings, int groups, long elapsedMs);
}
