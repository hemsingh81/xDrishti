namespace XDrishti.Api.Exports;

/// <summary>Structured log events of the exports (P8-20; ids 4940 to 4949). Kind, format, counts and a code only: never a symbol, a run key, a filter value or a number of the table.</summary>
internal static partial class ExportLogEvents
{
    [LoggerMessage(EventId = 4940, Level = LogLevel.Information, Message = "export.done {Kind} {Format}: {Rows} rows, {Bytes} bytes, {Ms} ms")]
    public static partial void Done(ILogger logger, string kind, string format, int rows, int bytes, long ms);

    [LoggerMessage(EventId = 4941, Level = LogLevel.Warning, Message = "export.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4942, Level = LogLevel.Error, Message = "export.audit_failed {Kind}: no file was given")]
    public static partial void AuditFailed(ILogger logger, string kind);
}
