using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>Changes the Data settings of an account (the live-feed budget).</summary>
public sealed record UpdateDataCommand(Guid AccountId, DataInput Input, string Actor) : ICommand;

internal sealed class UpdateDataHandler(
    IAccountStore accounts,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<UpdateDataHandler> logger) : ICommandHandler<UpdateDataCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(UpdateDataCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.NotFound);
        }

        var before = account.LiveFeedBudget;
        var now = clock.GetUtcNow();
        var configured = account.ConfigureData(command.Input.LiveFeedBudget, now);
        if (configured.IsFailure)
        {
            return Result.Failure<AccountDetail>(configured.Error);
        }

        events.Record(AccountEvent.Record(AccountEventType.DataUpdated, account.Id, $"Live-feed budget {before} -> {account.LiveFeedBudget}", command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.DataUpdated(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
    }
}
