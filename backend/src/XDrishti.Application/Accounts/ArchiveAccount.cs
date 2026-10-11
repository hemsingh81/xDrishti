using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>
/// Removes an account from lists and services. Its history and audit trail stay; its stored broker secrets and tokens are deleted, its
/// connection is reset to not connected and its pending broker sign-ins are deleted. The primary Data account cannot be removed.
/// </summary>
public sealed record ArchiveAccountCommand(Guid AccountId, string Actor) : ICommand;

internal sealed class ArchiveAccountHandler(
    IAccountStore accounts,
    DefaultBuckets defaultBuckets,
    IBrokerSecretVault vault,
    IBrokerConnectionStore connections,
    IPendingConnectStore pending,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ArchiveAccountHandler> logger) : ICommandHandler<ArchiveAccountCommand>
{
    public async ValueTask<Result> HandleAsync(ArchiveAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure(AccountErrors.NotFound);
        }

        var now = clock.GetUtcNow();
        var archived = account.Archive(now);
        if (archived.IsFailure)
        {
            return archived;
        }

        // An archived account never connects again: its secrets have no use left.
        var stored = await vault.ListAsync([account.Id], cancellationToken);
        foreach (var secret in stored.GetValueOrDefault(account.Id) ?? [])
        {
            await vault.DeleteAsync(account.Id, secret.Name, cancellationToken);
        }

        (await connections.FindAsync(account.Id, cancellationToken))?.Disconnect();
        await defaultBuckets.ArchiveAsync(account, now, cancellationToken);

        events.Record(AccountEvent.Record(AccountEventType.AccountArchived, account.Id, $"Account '{account.Name}' archived; history is kept (default portfolio bucket archived), stored secrets deleted (tokens included), broker connection cleared", command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure(AccountErrors.ConcurrentChange);
        }

        await pending.DeleteForAccountAsync(account.Id, cancellationToken);
        AccountsLog.AccountArchived(logger, account.Slug);
        return Result.Success();
    }
}
