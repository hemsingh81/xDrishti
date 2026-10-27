
namespace XDrishti.Mcp;

/// <summary>
/// Log events 6030 to 6035 (spec P9-02). Never SQL text, an argument value, a row, a token or an exception message: a call is logged by tool name, a hash of its arguments, the number of rows, the time and the status.
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

    [LoggerMessage(EventId = 6034, Level = LogLevel.Warning, Message = "mcp.unauthorized")]
    public static partial void Unauthorized(this ILogger logger);

    [LoggerMessage(EventId = 6035, Level = LogLevel.Warning, Message = "mcp.rate_limited {Client}")]
    public static partial void RateLimited(this ILogger logger, string client);
}
