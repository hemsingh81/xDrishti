using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>
/// Switches roles of an account on or off, or sets the Data kind (primary or standby). Switching Portfolio on creates the account's default bucket
/// if it does not exist yet (in the same save).
/// </summary>
public sealed record UpdateRolesCommand(Guid AccountId, RolesInput Input, string Actor) : ICommand;

internal sealed class UpdateRolesHandler(
    IAccountStore accounts,
    DefaultBuckets defaultBuckets,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<UpdateRolesHandler> logger) : ICommandHandler<UpdateRolesCommand, AccountDetail>
{
    /// <summary>A save that created the bucket and was refused has met a concurrent request doing the same: once more shows what it committed.</summary>
    private const int Attempts = 2;

    public async ValueTask<Result<AccountDetail>> HandleAsync(UpdateRolesCommand command, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var account = await accounts.FindAsync(command.AccountId, cancellationToken);
            if (account is null)
            {
                return Result.Failure<AccountDetail>(AccountErrors.NotFound);
            }

            var errors = new FieldErrors();
            var change = AccountInputs.ParseRoles(command.Input, errors);
            if (change is null)
            {
                return Result.Failure<AccountDetail>(AccountErrors.Invalid(errors));
            }

            var primary = await accounts.FindPrimaryDataAsync(cancellationToken);
            var standby = await accounts.FindStandbyDataAsync(cancellationToken);
            var before = AccountSummaries.Roles(account);
            var now = clock.GetUtcNow();
            var changed = account.ChangeRoles(change, primary, standby, now);
            if (changed.IsFailure)
            {
                return Result.Failure<AccountDetail>(changed.Error);
            }

            var ensured = await defaultBuckets.EnsureAsync(account, command.Actor, now, cancellationToken);
            if (ensured.IsFailure)
            {
                return Result.Failure<AccountDetail>(ensured.Error);
            }

            events.Record(AccountEvent.Record(AccountEventType.RolesChanged, account.Id, $"Roles: {before} -> {AccountSummaries.Roles(account)}", command.Actor, now));
            if (await unitOfWork.TrySaveChangesAsync(cancellationToken))
            {
                AccountsLog.RolesChanged(logger, account.Slug);
                return Result.Success(await views.DetailAsync(account, cancellationToken));
            }

            AccountsLog.ChangeCollided(logger, account.Slug);
            if (!ensured.Value || attempt >= Attempts)
            {
                return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
            }
        }
    }
}
