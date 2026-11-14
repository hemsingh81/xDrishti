using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Context;

/// <summary>Structured log events of the market context job (event ids 3391 to 3395: unique across the solution, asserted by the architecture tests). Counts, dates and codes only: no price, no token, no response text.</summary>
internal static partial class ContextLog
{
    [LoggerMessage(EventId = 3391, Level = LogLevel.Information, Message = "Market context refreshed: {Written} written, {Pending} waiting for the VIX, {Skipped} skipped, last day {LastDate}")]
    public static partial void Refreshed(ILogger logger, int written, int pending, int skipped, string lastDate);

    [LoggerMessage(EventId = 3392, Level = LogLevel.Information, Message = "Market context: a prerequisite is missing ({Missing}); nothing was written")]
    public static partial void PrerequisiteMissing(ILogger logger, string missing);

    [LoggerMessage(EventId = 3393, Level = LogLevel.Error, Message = "Market context upkeep after a data job failed")]
    public static partial void UpkeepCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3396, Level = LogLevel.Information, Message = "Context inbox: {Files} files, {Failed} failed, {IndexWritten} index days and {VixWritten} VIX closes written, {AlreadyStored} already stored, refused {Rejected}")]
    public static partial void InboxLoaded(ILogger logger, int files, int failed, int indexWritten, int vixWritten, int alreadyStored, string rejected);

    [LoggerMessage(EventId = 3397, Level = LogLevel.Error, Message = "Context inbox step failed")]
    public static partial void InboxStepFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3394, Level = LogLevel.Information, Message = "Breadth computed: {Days} days, {InsufficientDays} insufficient, {PendingDays} waiting, {Ms} ms")]
    public static partial void BreadthComputed(ILogger logger, int days, int insufficientDays, int pendingDays, long ms);

    [LoggerMessage(EventId = 3395, Level = LogLevel.Information, Message = "Breadth rebuilt: {Days} days looked at, {Changed} changed, {Written} written")]
    public static partial void BreadthRebuilt(ILogger logger, int days, int changed, bool written);
}
