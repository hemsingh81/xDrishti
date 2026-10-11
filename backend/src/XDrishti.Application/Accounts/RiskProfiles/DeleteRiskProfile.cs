using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.RiskProfiles;

/// <summary>Deletes a shared risk profile. Refused while an account uses it, and for the built-in profiles.</summary>
public sealed record DeleteRiskProfileCommand(Guid ProfileId, string Actor) : ICommand;

internal sealed class DeleteRiskProfileHandler(
    IRiskProfileStore profiles,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<DeleteRiskProfileHandler> logger) : ICommandHandler<DeleteRiskProfileCommand>
{
    public ValueTask<Result> HandleAsync(DeleteRiskProfileCommand command, CancellationToken cancellationToken) =>
        // One transaction with the profile row locked: the usage count cannot go stale before the delete commits.
        unitOfWork.RunSerializedAsync(token => DeleteAsync(command, token), cancellationToken);

    private async ValueTask<Result> DeleteAsync(DeleteRiskProfileCommand command, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindForDeleteAsync(command.ProfileId, cancellationToken);
        if (profile is null)
        {
            return Result.Failure(AccountErrors.RiskProfileNotFound);
        }

        var usage = await profiles.CountUsageAsync(cancellationToken);
        var allowed = profile.CheckCanBeDeleted(usage.GetValueOrDefault(profile.Id));
        if (allowed.IsFailure)
        {
            return allowed;
        }

        profiles.Remove(profile);
        events.Record(AccountEvent.Record(AccountEventType.RiskProfileDeleted, null, $"Risk profile '{profile.Name}' deleted", command.Actor, clock.GetUtcNow()));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, "a risk profile");
            return Result.Failure(AccountErrors.ConcurrentChange);
        }

        AccountsLog.RiskProfileDeleted(logger, profile.Name);
        return Result.Success();
    }
}
