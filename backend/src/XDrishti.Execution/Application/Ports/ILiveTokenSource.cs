using XDrishti.Domain.Accounts;

namespace XDrishti.Execution.Application.Ports;

/// <summary>
/// The live access token of one account, from the sealed same-day hand-over (ADR 0171). For the live readiness reads only: it is not an <c>IExecCredentials</c> and the order client never sees it (ADR 0157).
/// A token is given only while the account's connection is Connected with an expiry in the future and the row decrypts for that account with the hand-over key; anything else (no row, not connected, expired,
/// another key, a changed row, a read failure, no key) is null, which the caller answers as <c>token_unavailable</c>.
/// </summary>
internal interface ILiveTokenSource
{
    Task<SecretValue?> FindAsync(Guid accountId, CancellationToken cancellationToken);
}
