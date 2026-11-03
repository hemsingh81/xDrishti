using Microsoft.Extensions.Logging;

namespace XDrishti.Application.OptionExpression;

/// <summary>Structured log events of the expression evaluation (P14-09; ids 7070 and 7071). Family, verdict word, ids and counts only: never a price, a trade or a figure.</summary>
internal static partial class ExpressionEvalLog
{
    [LoggerMessage(EventId = 7070, Level = LogLevel.Information, Message = "expression.evaluated {Family} {VersionKey}: {Verdict}, {Rows} paired trades, new {IsNew}")]
    public static partial void Evaluated(ILogger logger, string family, string versionKey, string verdict, int rows, bool isNew);

    [LoggerMessage(EventId = 7071, Level = LogLevel.Warning, Message = "expression.refused {Family} {Code}")]
    public static partial void Refused(ILogger logger, string family, string code);
}
