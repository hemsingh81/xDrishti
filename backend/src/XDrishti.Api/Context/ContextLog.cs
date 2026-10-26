namespace XDrishti.Api.Context;

/// <summary>The log lines of the <c>context</c> verb (P3-01d; event ids 4487 and 4488, the next free ones after 4486; unique across the solution, asserted by the architecture tests). Counts and codes only.</summary>
internal static partial class ContextLog
{
    [LoggerMessage(EventId = 4487, Level = LogLevel.Information, Message = "Market context refreshed by the verb: {Written} written, {Pending} waiting, {Skipped} skipped")]
    public static partial void Refreshed(ILogger logger, int written, int pending, int skipped);

    [LoggerMessage(EventId = 4488, Level = LogLevel.Information, Message = "Market context coverage shown: {Days} days, {Skipped} skipped")]
    public static partial void Shown(ILogger logger, int days, int skipped);
}
