using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Selection;

/// <summary>Structured log events of the basket settings, clusters and basket context (BK-08; ids 7390 to 7399). Counts, codes and ids only: never a password, an actor's secret or an amount of capital.</summary>
internal static partial class BasketSettingsLog
{
    [LoggerMessage(EventId = 7390, Level = LogLevel.Information, Message = "basket_settings.changed {Rows} rows, {Loosened} loosening, version {Version}")]
    public static partial void Changed(ILogger logger, int rows, int loosened, long version);

    [LoggerMessage(EventId = 7391, Level = LogLevel.Warning, Message = "basket_settings.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 7392, Level = LogLevel.Warning, Message = "basket_settings.audit_failed {Failure}")]
    public static partial void AuditFailed(ILogger logger, string failure);

    [LoggerMessage(EventId = 7393, Level = LogLevel.Information, Message = "clusters.built date {Date}: {Symbols} symbols, {Clusters} clusters, {Unknown} unknown")]
    public static partial void ClustersBuilt(ILogger logger, DateOnly date, int symbols, int clusters, int unknown);

    [LoggerMessage(EventId = 7394, Level = LogLevel.Information, Message = "basket_context.written basket {BasketId} date {Date} state {State}")]
    public static partial void ContextWritten(ILogger logger, Guid basketId, DateOnly date, string state);
}
