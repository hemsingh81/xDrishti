using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.Connections;

/// <summary>Stable error codes of connecting an account to Dhan (part of the API contract, also used as <c>connectError</c> after the Dhan sign-in).</summary>
public static class ConnectionErrors
{
    /// <summary>502: Dhan refused, failed or could not be reached. The message names at most Dhan's error code.</summary>
    public const string BrokerErrorCode = "accounts.broker_error";

    /// <summary>409: the token belongs to another client id than the account's; nothing was stored.</summary>
    public const string ConnectMismatchCode = "accounts.connect_mismatch";

    /// <summary>400: the sign-in came back too late, twice or to another session.</summary>
    public const string ConnectExpiredCode = "accounts.connect_expired";

    /// <summary>400: Dhan sent the owner back without a tokenId (sign-in cancelled or refused at Dhan).</summary>
    public const string ConnectCancelledCode = "accounts.connect_cancelled";

    /// <summary>429 with retryAfterSeconds: Dhan's daily sign-in consents for the account are used up (25 per day).</summary>
    public const string ConsentLimitCode = "accounts.consent_limit";

    /// <summary>409: the API key or secret is not set yet.</summary>
    public const string AppNotConfiguredCode = "accounts.app_not_configured";

    public static readonly Error ConnectMismatch = Error.Conflict(
        ConnectMismatchCode, "The Dhan sign-in belongs to another client id than this account's. Nothing was stored.");

    public static readonly Error AppNotConfigured = Error.Conflict(
        AppNotConfiguredCode, "Enter the Dhan API key and secret first (Connection tab, Replace).");

    public static Error Broker(BrokerFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return Error.Upstream(BrokerErrorCode, ConnectionTexts.Failure(failure) + (failure.Kind == BrokerFailureKind.Unavailable ? " Try again in a moment." : string.Empty));
    }

    public static Error ConsentLimit(int limit, int retryAfterSeconds) => Error.RateLimited(
        ConsentLimitCode, $"Dhan allows {limit} sign-in consents a day for an account; they are used up. Try again tomorrow (India time).", retryAfterSeconds);
}
