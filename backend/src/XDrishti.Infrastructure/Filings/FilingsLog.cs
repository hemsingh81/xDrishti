using Microsoft.Extensions.Logging;

namespace XDrishti.Infrastructure.Filings;

/// <summary>Structured log events of the filings source (event ids 6981 to 6985): counts and closed reason codes only, never a file name's content, a file's bytes or a sidecar's <c>source_ref</c>.</summary>
internal static partial class FilingsLog
{
    [LoggerMessage(EventId = 6981, Level = LogLevel.Information, Message = "filings.scan accepted={Accepted} rejected={Rejected} truncated={Truncated} problems={Problems} reasons={Reasons}")]
    public static partial void Scan(ILogger logger, int accepted, int rejected, bool truncated, string problems, string reasons);
}
