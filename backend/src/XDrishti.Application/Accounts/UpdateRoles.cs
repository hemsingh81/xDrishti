using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>Switches roles of an account on or off, or sets the Data kind (primary or standby).</summary>
public sealed record UpdateRolesCommand(Guid AccountId, RolesInput Input, string Actor) : ICommand;

internal sealed class UpdateRolesHandler(
    IAccountStore accounts,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<UpdateRolesHandler> logger) : ICommandHandler<UpdateRolesCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(UpdateRolesCommand command, CancellationToken cancellationToken)
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

        events.Record(AccountEvent.Record(AccountEventType.RolesChanged, account.Id, $"Roles: {before} -> {AccountSummaries.Roles(account)}", command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.RolesChanged(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
    }
}
