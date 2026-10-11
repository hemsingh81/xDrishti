using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>
/// Changes name, client id, colour and notes of an account. A new client id
/// clears the broker connection (stored tokens, status, open sign-ins): a token belongs to the client id it was issued for.
/// </summary>
public sealed record UpdateConnectionCommand(Guid AccountId, ConnectionInput Input, string Actor) : ICommand;

internal sealed class UpdateConnectionHandler(
    IAccountStore accounts,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    ConnectionWriter connection,
    IPendingConnectStore pending,
    AccountViews views,
    TimeProvider clock,
    ILogger<UpdateConnectionHandler> logger) : ICommandHandler<UpdateConnectionCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(UpdateConnectionCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.NotFound);
        }

        var input = command.Input;
        var before = (account.Name, account.Color, account.Notes, account.ClientId);
        var now = clock.GetUtcNow();
        var updated = account.UpdateConnection(input.Name, input.ClientId, input.Color, input.Notes, now);
        if (updated.IsFailure)
        {
            return Result.Failure<AccountDetail>(updated.Error);
        }

        var clientIdChanged = before.ClientId != account.ClientId;
        var cleared = clientIdChanged && await connection.ClearAsync(account.Id, cancellationToken);
        events.Record(AccountEvent.Record(AccountEventType.ConnectionUpdated, account.Id, Describe(before, account, cleared), command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        if (clientIdChanged)
        {
            // A sign-in started for the old client id must not complete for the new one.
            await pending.CloseUnusedForAccountAsync(account.Id, now, cancellationToken);
        }

        AccountsLog.ConnectionUpdated(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
    }

    /// <summary>What changed, without ever naming the client id's value.</summary>
    private static string Describe((string Name, string Color, string? Notes, string ClientId) before, BrokerAccount after, bool connectionCleared)
    {
        var changes = new List<string>();
        if (before.Name != after.Name)
        {
            changes.Add($"name '{before.Name}' -> '{after.Name}'");
        }

        if (before.ClientId != after.ClientId)
        {
            changes.Add(connectionCleared ? "client id changed; broker connection cleared" : "client id changed");
        }

        if (before.Color != after.Color)
        {
            changes.Add("colour changed");
        }

        if (before.Notes != after.Notes)
        {
            changes.Add("notes changed");
        }

        return changes.Count == 0 ? "Connection saved without changes" : "Connection: " + string.Join("; ", changes);
    }
}
