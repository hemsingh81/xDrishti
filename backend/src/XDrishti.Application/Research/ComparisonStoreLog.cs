using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Research;

/// <summary>
/// Structured log events of the comparison store (spec P4-05, D10; event ids 4425 to 4427, unique across the solution, asserted by the architecture tests). The comparison key (first 12 characters), counts, codes and milliseconds only: never a
/// trade, a table or a candidate's parameters.
/// </summary>
internal static partial class ComparisonStoreLog
{
    [LoggerMessage(EventId = 4425, Level = LogLevel.Information, Message = "store.comparison.completed comparison {ComparisonKey}: {Members} members, {Outcome}, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, string comparisonKey, int members, string outcome, long totalMs);

    [LoggerMessage(EventId = 4426, Level = LogLevel.Error, Message = "store.comparison.conflict comparison {ComparisonKey}: another record is stored under this key; differing figures: {Differing}")]
    public static partial void Conflict(ILogger logger, string comparisonKey, string differing);

    [LoggerMessage(EventId = 4427, Level = LogLevel.Error, Message = "store.comparison.failed comparison {ComparisonKey}: {Code}")]
    public static partial void Failed(ILogger logger, string comparisonKey, string code);
}
