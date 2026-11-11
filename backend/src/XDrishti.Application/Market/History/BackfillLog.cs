using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.History;

/// <summary>Structured log events of the 1-minute backfill (event ids 7520 to 7529: unique across the solution, asserted by the architecture tests). Counts, symbols and dates only: never a token, a URL or the broker's answer.</summary>
internal static partial class BackfillLog
{
    [LoggerMessage(EventId = 7520, Level = LogLevel.Warning, Message = "Backfill {Symbol} {From} to {To}: attempt {Attempts} failed")]
    public static partial void ChunkFailed(ILogger logger, string symbol, DateOnly from, DateOnly to, int attempts);

    [LoggerMessage(EventId = 7521, Level = LogLevel.Error, Message = "Backfill {Symbol} {From} to {To}: the bars could not be stored")]
    public static partial void ChunkCrashed(ILogger logger, string symbol, DateOnly from, DateOnly to, Exception exception);

    [LoggerMessage(EventId = 7522, Level = LogLevel.Warning, Message = "Backfill {Symbol}: {BeyondSeam} bars at or after the first CSV bar were dropped")]
    public static partial void BeyondSeam(ILogger logger, string symbol, int beyondSeam);

    [LoggerMessage(EventId = 7523, Level = LogLevel.Warning, Message = "Backfill {Symbol}: the seam is a jump of {MovePct:F1} % with no applied action")]
    public static partial void SeamJump(ILogger logger, string symbol, double movePct);

    [LoggerMessage(EventId = 7524, Level = LogLevel.Information, Message = "Seam probe {Symbol}: {Verdict} (median {MedianBps:F2} bps, {WithinPct:F1} % within the limit, {Compared} minutes)")]
    public static partial void ProbeDone(ILogger logger, string symbol, string verdict, double medianBps, double withinPct, long compared);

    [LoggerMessage(EventId = 7525, Level = LogLevel.Warning, Message = "Backfill {Symbol}: the first CSV bar moved earlier than the recorded seam; the instrument needs a new probe")]
    public static partial void SeamMoved(ILogger logger, string symbol);
}
