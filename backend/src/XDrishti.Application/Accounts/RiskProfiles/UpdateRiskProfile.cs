using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.RiskProfiles;

/// <summary>Replaces the name and values of a shared risk profile. Accounts that use it follow immediately.</summary>
public sealed record UpdateRiskProfileCommand(Guid ProfileId, RiskProfileInput Input, string Actor) : ICommand;

internal sealed class UpdateRiskProfileHandler(
    IRiskProfileStore profiles,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<UpdateRiskProfileHandler> logger) : ICommandHandler<UpdateRiskProfileCommand, RiskProfileView>
{
    public async ValueTask<Result<RiskProfileView>> HandleAsync(UpdateRiskProfileCommand command, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindAsync(command.ProfileId, cancellationToken);
        if (profile is null)
        {
            return Result.Failure<RiskProfileView>(AccountErrors.RiskProfileNotFound);
        }

        var errors = new FieldErrors();
        var limits = RiskProfileMapping.ParseLimits(command.Input, errors);
        if (limits is null)
        {
            return Result.Failure<RiskProfileView>(AccountErrors.Invalid(errors));
        }

        var before = (profile.Name, profile.Limits);
        var now = clock.GetUtcNow();
        var updated = profile.Update(command.Input.Name, limits, now);
        if (updated.IsFailure)
        {
            return Result.Failure<RiskProfileView>(updated.Error);
        }

        if (await profiles.NameExistsAsync(profile.Name, profile.Id, cancellationToken))
        {
            return Result.Failure<RiskProfileView>(AccountErrors.Conflict($"A risk profile named '{profile.Name}' already exists."));
        }

        events.Record(AccountEvent.Record(AccountEventType.RiskProfileUpdated, null, RiskProfileChanges.Describe(before.Name, before.Limits, profile), command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, "a risk profile");
            return Result.Failure<RiskProfileView>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.RiskProfileUpdated(logger, profile.Name);
        var usage = await profiles.CountUsageAsync(cancellationToken);
        return Result.Success(RiskProfileMapping.ToView(profile, usage.GetValueOrDefault(profile.Id)));
    }
}
