using Microsoft.Extensions.Logging;

namespace XDrishti.Application.OptionExpression;

/// <summary>Structured log events of the option re-mark (P14-08; ids 7060 and 7061). Counts and ids only: never a bar, a price or a trade.</summary>
internal static partial class RemarkLog
{
    [LoggerMessage(EventId = 7060, Level = LogLevel.Information, Message = "expression.remark.start run {CashRun} profile {Profile}: {Trades} trades")]
    public static partial void Start(ILogger logger, string cashRun, string profile, int trades);

    [LoggerMessage(EventId = 7061, Level = LogLevel.Information, Message = "expression.remark.done {RunId}: {Trades} trades, {Paired} paired, stored {Stored}")]
    public static partial void Done(ILogger logger, string runId, int trades, int paired, bool stored);
}
