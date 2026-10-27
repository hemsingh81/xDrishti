using Microsoft.Extensions.Logging;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>Log events 6020 and 6021 (spec P9-02): the provisioning of the two read-only roles by <c>xd-api migrate</c>. Never a password, a connection string or an exception message.</summary>
internal static partial class AiReadOnlyLog
{
    [LoggerMessage(EventId = 6020, Level = LogLevel.Warning, Message = "ai.roles_nologin: {Reason}; the tool server cannot log in until the password files exist and migrate runs again")]
    public static partial void RolesLeftNoLogin(this ILogger logger, string reason);

    [LoggerMessage(EventId = 6021, Level = LogLevel.Information, Message = "ai.roles_provisioned: the read-only roles have a login")]
    public static partial void RolesProvisioned(this ILogger logger);
}
