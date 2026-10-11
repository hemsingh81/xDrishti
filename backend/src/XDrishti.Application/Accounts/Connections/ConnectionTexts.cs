using System.Globalization;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Accounts.Connections;

/// <summary>Short, safe texts for errors, the connection's last error and the audit trail: times and Dhan error codes only.</summary>
internal static class ConnectionTexts
{
    /// <summary>For example <c>Dhan refused the request (error DH-901).</c></summary>
    public static string Failure(BrokerFailure failure)
    {
        var code = failure.BrokerCode is { } c ? $" (error {c})" : string.Empty;
        return failure.Kind is BrokerFailureKind.Rejected or BrokerFailureKind.Unauthorized
            ? $"Dhan refused the request{code}."
            : $"Dhan could not be reached or gave an unexpected answer{code}.";
    }

    /// <summary>For example <c>2026-10-12 15:30 IST</c>.</summary>
    private static string IndiaTime(DateTimeOffset at) =>
        at.ToOffset(BrokerTokenExpiry.IndiaOffset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " IST";

    /// <summary>For example <c>Connected by signing in at Dhan as Example Name; valid until 2026-10-12 15:30 IST</c>.</summary>
    public static string Connected(BrokerConnection connection)
    {
        var holder = connection.BrokerUserName is { } name ? $" as {name}" : string.Empty;
        var until = connection.ExpiresAt is { } at ? IndiaTime(at) : "unknown";
        return $"Connected by signing in at Dhan{holder}; valid until {until}";
    }
}
