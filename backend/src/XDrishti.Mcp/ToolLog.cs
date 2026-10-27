
namespace XDrishti.Mcp;

/// <summary>
/// Log events 6030 to 6035 (spec P9-02) and 6120 to 6122 (spec P9-05). Never SQL text, an argument value, a row, a token or an exception message: a call is logged by tool name, a hash of its arguments, the number of rows, the time and the status.
/// </summary>
internal static partial class ToolLog
{
    [LoggerMessage(EventId = 6030, Level = LogLevel.Information, Message = "mcp.call {Tool} args={ArgumentHash} rows={Rows} ms={Elapsed} status={Status}")]
    public static partial void Call(this ILogger logger, string tool, string argumentHash, int rows, long elapsed, string status);

    [LoggerMessage(EventId = 6031, Level = LogLevel.Warning, Message = "mcp.refused {Tool} {Code}")]
    public static partial void Refused(this ILogger logger, string tool, string code);

    [LoggerMessage(EventId = 6032, Level = LogLevel.Information, Message = "mcp.sql_rejected {Reason}")]
    public static partial void SqlRejected(this ILogger logger, string reason);

    [LoggerMessage(EventId = 6033, Level = LogLevel.Information, Message = "mcp.flags {Tool} redactions={Redactions} injection_flags={InjectionFlags}")]
    public static partial void Flags(this ILogger logger, string tool, int redactions, int injectionFlags);

    [LoggerMessage(EventId = 6120, Level = LogLevel.Information, Message = "mcp.report {Report} status={Status} ms={Elapsed}")]
    public static partial void Report(this ILogger logger, string report, string status, long elapsed);

    [LoggerMessage(EventId = 6121, Level = LogLevel.Information, Message = "mcp.data {Tool} rows={Rows} ms={Elapsed}")]
    public static partial void Data(this ILogger logger, string tool, int rows, long elapsed);

    [LoggerMessage(EventId = 6122, Level = LogLevel.Information, Message = "mcp.calc_rejected {Reason}")]
    public static partial void CalcRejected(this ILogger logger, string reason);

    [LoggerMessage(EventId = 6034, Level = LogLevel.Debug, Message = "mcp.unauthorized")]
    public static partial void Unauthorized(this ILogger logger);

    [LoggerMessage(EventId = 6035, Level = LogLevel.Warning, Message = "mcp.rate_limited {Client}")]
    public static partial void RateLimited(this ILogger logger, string client);
}
