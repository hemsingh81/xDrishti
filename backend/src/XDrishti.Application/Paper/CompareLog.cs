using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Paper;

/// <summary>Structured log events of the backtest-versus-paper report (P10-04; ids 5246 to 5254). Counts, ids and codes only: no price, no symbol, no account.</summary>
internal static partial class CompareLog
{
    [LoggerMessage(EventId = 5246, Level = LogLevel.Information, Message = "paper.compare epoch {EpochId}: {Counted} counted, {Reserve} reserve, {Straddle} straddle, {Open} open")]
    public static partial void Read(ILogger logger, long epochId, int counted, int reserve, int straddle, int open);

    [LoggerMessage(EventId = 5247, Level = LogLevel.Warning, Message = "paper.compare epoch {EpochId}: no cell snapshot before {Start}; the band is unavailable")]
    public static partial void NoSnapshot(ILogger logger, long epochId, DateOnly start);

    [LoggerMessage(EventId = 5248, Level = LogLevel.Warning, Message = "paper.compare snapshot {Snapshot}: its source runs cannot be read ({Code}); the band is unavailable")]
    public static partial void ReferenceUnreadable(ILogger logger, string snapshot, string code);

    [LoggerMessage(EventId = 5249, Level = LogLevel.Warning, Message = "paper.compare epoch {EpochId}: basis differs in {Cells} cell(s)")]
    public static partial void BasisDiffers(ILogger logger, long epochId, int cells);

    [LoggerMessage(EventId = 5250, Level = LogLevel.Warning, Message = "paper.compare epoch {EpochId}: {Cells} cell(s) without a usable reference")]
    public static partial void ThinReference(ILogger logger, long epochId, int cells);

    [LoggerMessage(EventId = 5251, Level = LogLevel.Information, Message = "paper.compare epoch {EpochId}: band drawn, n {N}, {Draws} draws, position {Position}, drawdown {Drawdown}, {ElapsedMs} ms")]
    public static partial void Drawn(ILogger logger, long epochId, int n, int draws, string position, string drawdown, long elapsedMs);

    [LoggerMessage(EventId = 5252, Level = LogLevel.Warning, Message = "paper.compare epoch {EpochId}: early warning, paper is clearly worse than the backtest allows")]
    public static partial void EarlyWarning(ILogger logger, long epochId);

    [LoggerMessage(EventId = 5253, Level = LogLevel.Information, Message = "paper.compare epoch {EpochId}: expectancy {Expectancy}, drawdown {Drawdown}")]
    public static partial void Criteria(ILogger logger, long epochId, string expectancy, string drawdown);

    [LoggerMessage(EventId = 5254, Level = LogLevel.Information, Message = "paper.compare refused: {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
