using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Options;

/// <summary>
/// Structured log events of the option archive's reference data (spec P14-01). Counts only: no symbol list, price or file content. The counts of a refresh live
/// here, not in the run's warnings, which carry only real problems (ADR 0175).
/// </summary>
internal static partial class OptionsLog
{
    [LoggerMessage(EventId = 6901, Level = LogLevel.Information, Message = "options.contracts_applied: {Kept} kept for {Underlyings} underlyings, {Inserted} new, {Advanced} seen again, {Conflicts} conflicting, {Reused} reused ids, {LotChanges} lot changes, {Mixed} with mixed lots, {NotListed} not listed, {Unmapped} unmapped, {Invalid} invalid")]
    public static partial void ContractsApplied(ILogger logger, int kept, int underlyings, int inserted, int advanced, int conflicts, int reused, int lotChanges, int mixed, long notListed, int unmapped, long invalid);

    [LoggerMessage(EventId = 6902, Level = LogLevel.Warning, Message = "options.underlyings_unknown: {Count} listed underlyings are not an active index or share of the master list")]
    public static partial void UnknownUnderlyings(ILogger logger, int count);

    [LoggerMessage(EventId = 6903, Level = LogLevel.Information, Message = "options.underlyings_fixed: the default list was written once ({Indices} indices, {Shares} shares)")]
    public static partial void DefaultFixed(ILogger logger, int indices, int shares);

    [LoggerMessage(EventId = 6904, Level = LogLevel.Error, Message = "options.step_failed: the option contracts were not updated ({Stage}); the master list is not affected")]
    public static partial void StepFailed(ILogger logger, string stage, Exception exception);

    [LoggerMessage(EventId = 6905, Level = LogLevel.Information, Message = "options.underlyings_not_fixed: {Shares} ranked F&O shares of {Wanted}; the default list is used for this refresh and not written")]
    public static partial void DefaultNotFixed(ILogger logger, int shares, int wanted);
}
