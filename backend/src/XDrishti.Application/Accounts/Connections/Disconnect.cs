using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.Connections;

/// <summary>
/// Deletes the account's stored access token and marks it not connected (local only: Dhan documents no logout for these tokens; they end at
/// their expiry). Nothing to do when the account is not connected.
/// </summary>
public sealed record DisconnectCommand(Guid AccountId, string Actor) : ICommand;

internal sealed class DisconnectHandler(
    IAccountStore accounts,
    IBrokerSecretVault vault,
    IBrokerConnectionStore connections,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    ConnectionWriter writer,
    AccountViews views,
    TimeProvider clock,
    ILogger<DisconnectHandler> logger) : ICommandHandler<DisconnectCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(DisconnectCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindReadOnlyAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.NotFound);
        }

        var connection = await connections.FindAsync(account.Id, cancellationToken);
        var token = await vault.GetAsync(account.Id, BrokerSecretName.AccessToken, cancellationToken);
        if ((connection is null || connection.Status == ConnectionStatus.NotConnected) && token is null)
        {
            return Result.Success(await views.DetailAsync(account, cancellationToken));
        }

        var now = clock.GetUtcNow();
        await writer.DeleteTokenAsync(account.Id, cancellationToken);
        connection?.Disconnect();
        events.Record(AccountEvent.Record(AccountEventType.BrokerDisconnected, account.Id, "Disconnected from Dhan; stored access token deleted", command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.Disconnected(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
    }
}
