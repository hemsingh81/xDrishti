using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Pairs;

/// <summary>Structured log events of the pairs study (P14-13; ids 7050 to 7053). Family, quarter, verdict word and counts only: never a price, a symbol list or a trade.</summary>
internal static partial class PairsLog
{
    [LoggerMessage(EventId = 7050, Level = LogLevel.Information, Message = "pairs.formation {Quarter}: candidates {Candidates}, selected {Selected}, stored {Stored}")]
    public static partial void Formed(ILogger logger, string quarter, int candidates, int selected, bool stored);

    [LoggerMessage(EventId = 7051, Level = LogLevel.Information, Message = "pairs.run done: {Formations} formations, {Trades} trades written, N {Trials}")]
    public static partial void RunDone(ILogger logger, int formations, int trades, int trials);

    [LoggerMessage(EventId = 7052, Level = LogLevel.Information, Message = "pairs.eval {Family} {VersionKey}: {Verdict}, {Rows} trades, new {IsNew}")]
    public static partial void Evaluated(ILogger logger, string family, string versionKey, string verdict, int rows, bool isNew);

    [LoggerMessage(EventId = 7053, Level = LogLevel.Warning, Message = "pairs.refused {Family} {Code}")]
    public static partial void Refused(ILogger logger, string family, string code);
}
