using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>
/// The sealed same-day hand-over of an account's access token to the execution service (ADR 0171): one row per account in <c>accounts.exec_token_handover</c>,
/// sealed with its own key (not the vault key). Written and removed only together with the vault's access token: writes are collected and saved by
/// <see cref="IAccountUnitOfWork"/> in the same unit of work as the vault row and the connection, so there is no connect without a hand-over and no hand-over
/// left after a disconnect. Implementations must never log, return or put the token into exceptions.
/// </summary>
public interface IExecTokenHandover
{
    /// <summary>Seals the access token for the account, replacing an existing hand-over (a new login replaces the old one).</summary>
    ValueTask SetAsync(Guid accountId, SecretValue accessToken, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Removes the account's hand-over; nothing happens when there is none.</summary>
    ValueTask DeleteAsync(Guid accountId, CancellationToken cancellationToken);
}
