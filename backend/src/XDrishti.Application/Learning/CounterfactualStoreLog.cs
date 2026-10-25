using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the stored counterfactual (P8-12; ids 4880 to 4889). Counts and codes only.</summary>
internal static partial class CounterfactualStoreLog
{
    [LoggerMessage(EventId = 4880, Level = LogLevel.Information, Message = "learning.counterfactual.stored {Outcome}, {Windows} windows, created {Created}")]
    public static partial void Stored(ILogger logger, string outcome, int windows, bool created);

    [LoggerMessage(EventId = 4881, Level = LogLevel.Warning, Message = "learning.counterfactual.store_refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
