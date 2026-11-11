using Microsoft.Extensions.Logging;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>Log events 6020 and 6021 (spec P9-02): the provisioning of the two read-only roles by <c>xd-api migrate</c>. Never a password, a connection string or an exception message.</summary>
internal static partial class AiReadOnlyLog
{
    [LoggerMessage(EventId = 6020, Level = LogLevel.Warning, Message = "ai.roles_nologin: {Reason}; the tool server cannot log in until the password files exist and migrate runs again")]
    public static partial void RolesLeftNoLogin(this ILogger logger, string reason);

    [LoggerMessage(EventId = 6021, Level = LogLevel.Information, Message = "ai.roles_provisioned: the read-only roles have a login")]
    public static partial void RolesProvisioned(this ILogger logger);

    /// <summary>Log event 6124 (BK-12b): one call of the tool server to the API for a basket report. The route name, the status code (0 when no answer came) and the time; never a basket name, a symbol, the query or the token.</summary>
    [LoggerMessage(EventId = 6124, Level = LogLevel.Information, Message = "mcp.api_call {Route} status={Status} ms={Elapsed}")]
    public static partial void ApiCall(this ILogger logger, string route, int status, long elapsed);
}
