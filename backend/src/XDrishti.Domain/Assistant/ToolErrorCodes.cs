namespace XDrishti.Domain.Assistant;

/// <summary>The error codes of the tool contract (docs/specs/P9-00). Fixed vocabulary; a message never carries an argument value, SQL text, a row or a token.</summary>
public static class ToolErrorCodes
{
    public const string Disabled = "assistant.disabled";
    public const string InvalidArguments = "tool.invalid_arguments";
    public const string Timeout = "tool.timeout";
    public const string TooLarge = "tool.too_large";
    public const string Unavailable = "tool.unavailable";
    public const string Failed = "tool.failed";
    public const string SqlRejected = "sql.rejected";
    public const string SqlTimeout = "sql.timeout";
    public const string SqlDenied = "sql.denied";
    public const string SqlError = "sql.error";
    public const string TableUnknown = "table.unknown";
}
