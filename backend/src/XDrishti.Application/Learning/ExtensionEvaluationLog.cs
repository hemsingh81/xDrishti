using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the extension evaluations (P15-10; ids 7020 to 7023). Family, protocol, verdict word, counts and milliseconds only: never a trade, a price or a fact.</summary>
internal static partial class ExtensionEvaluationLog
{
    [LoggerMessage(EventId = 7020, Level = LogLevel.Information, Message = "learning.extension_eval.stored {Family} {Protocol} {VersionKey}: {Verdict}, {Rows} rows, new {IsNew}")]
    public static partial void Stored(ILogger logger, string family, string protocol, string versionKey, string verdict, int rows, bool isNew);

    [LoggerMessage(EventId = 7021, Level = LogLevel.Warning, Message = "learning.extension_eval.refused {Family} {Code}")]
    public static partial void Refused(ILogger logger, string family, string code);

    [LoggerMessage(EventId = 7022, Level = LogLevel.Information, Message = "learning.extension_eval.auto {Action} {Reason}")]
    public static partial void AutoRan(ILogger logger, string action, string reason);

    [LoggerMessage(EventId = 7023, Level = LogLevel.Information, Message = "learning.extension_eval.holdout_not_read {Family}: {Reason}")]
    public static partial void HoldOutNotRead(ILogger logger, string family, string reason);
}
