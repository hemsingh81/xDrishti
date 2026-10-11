using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>
/// Makes an account the primary Data account. The previous primary becomes the standby when that slot is free, otherwise it loses the
/// Data role. Done in one transaction that is serialised against other switches, so there is always exactly one primary.
/// </summary>
public sealed record MakePrimaryDataCommand(Guid AccountId, string Actor) : ICommand;

internal sealed class MakePrimaryDataHandler(
    IAccountStore accounts,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<MakePrimaryDataHandler> logger) : ICommandHandler<MakePrimaryDataCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(MakePrimaryDataCommand command, CancellationToken cancellationToken)
    {
        var outcome = await unitOfWork.RunSerializedAsync(ct => SwapAsync(command, ct), cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<AccountDetail>(outcome.Error);
        }

        var account = await accounts.FindReadOnlyAsync(command.AccountId, cancellationToken);
        return account is null
            ? Result.Failure<AccountDetail>(AccountErrors.NotFound)
            : Result.Success(await views.DetailAsync(account, cancellationToken));
    }

    private async ValueTask<Result> SwapAsync(MakePrimaryDataCommand command, CancellationToken cancellationToken)
    {
        // Everything is read inside the serialised section: a concurrent switch that finished before us is seen here.
        var target = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (target is null)
        {
            return Result.Failure(AccountErrors.NotFound);
        }

        var primary = await accounts.FindPrimaryDataAsync(cancellationToken);
        var standby = await accounts.FindStandbyDataAsync(cancellationToken);
        var planned = PrimaryDataSwap.Plan(target, primary, standby);
        if (planned.IsFailure)
        {
            return Result.Failure(planned.Error);
        }

        var swap = planned.Value;
        if (swap.IsNoOp)
        {
            return Result.Success();
        }

        var now = clock.GetUtcNow();
        var previousName = swap.Previous?.Name;

        // Step 1 frees the flags, step 2 sets the new ones: saved one after the other (same transaction) so that the database's
        // "one primary / one standby" rules hold after every statement.
        swap.Release(now);
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, target.Slug);
            return Result.Failure(AccountErrors.ConcurrentChange);
        }

        swap.Complete(now);
        var previousText = previousName is null
            ? "no previous primary"
            : $"'{previousName}' is now {(swap.PreviousBecomesStandby ? "standby" : "no longer a Data account")}";
        events.Record(AccountEvent.Record(AccountEventType.PrimaryDataChanged, target.Id, $"'{target.Name}' is now the primary Data account; {previousText}", command.Actor, now));
        if (swap.Previous is { } previous)
        {
            events.Record(AccountEvent.Record(AccountEventType.PrimaryDataChanged, previous.Id, $"'{target.Name}' became the primary Data account; this account is now {(swap.PreviousBecomesStandby ? "standby" : "off")}", command.Actor, now));
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, target.Slug);
            return Result.Failure(AccountErrors.ConcurrentChange);
        }

        AccountsLog.PrimaryDataChanged(logger, target.Slug);
        return Result.Success();
    }
}
