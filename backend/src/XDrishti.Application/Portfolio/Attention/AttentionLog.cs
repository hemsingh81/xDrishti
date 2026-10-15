using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Attention;

/// <summary>Structured log events of the attention items (event ids 4321 to 4324). Counts only.</summary>
internal static partial class AttentionLog
{
    [LoggerMessage(EventId = 4321, Level = LogLevel.Warning, Message = "The attention items could not be reconciled; the notices keep what was remembered")]
    public static partial void ReconcileFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4322, Level = LogLevel.Warning, Message = "The resolved attention items past their retention could not be deleted")]
    public static partial void RetentionFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4323, Level = LogLevel.Information, Message = "Deleted {Count} resolved attention items older than {Days} days")]
    public static partial void Pruned(ILogger logger, int count, int days);

    [LoggerMessage(EventId = 4324, Level = LogLevel.Warning, Message = "{Count} derived attention items repeated an id; the last of each was kept")]
    public static partial void DuplicateIds(ILogger logger, int count);
}
