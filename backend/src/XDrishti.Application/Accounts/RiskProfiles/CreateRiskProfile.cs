using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.RiskProfiles;

/// <summary>Creates a shared risk profile.</summary>
public sealed record CreateRiskProfileCommand(RiskProfileInput Input, string Actor) : ICommand;

internal sealed class CreateRiskProfileHandler(
    IRiskProfileStore profiles,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<CreateRiskProfileHandler> logger) : ICommandHandler<CreateRiskProfileCommand, RiskProfileView>
{
    public async ValueTask<Result<RiskProfileView>> HandleAsync(CreateRiskProfileCommand command, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var limits = RiskProfileMapping.ParseLimits(command.Input, errors);
        if (limits is null)
        {
            return Result.Failure<RiskProfileView>(AccountErrors.Invalid(errors));
        }

        var now = clock.GetUtcNow();
        var created = RiskProfile.Create(command.Input.Name, limits, now);
        if (created.IsFailure)
        {
            return Result.Failure<RiskProfileView>(created.Error);
        }

        var profile = created.Value;
        if (await profiles.NameExistsAsync(profile.Name, null, cancellationToken))
        {
            return Result.Failure<RiskProfileView>(AccountErrors.Conflict($"A risk profile named '{profile.Name}' already exists."));
        }

        profiles.Add(profile);
        events.Record(AccountEvent.Record(AccountEventType.RiskProfileCreated, null, $"Risk profile '{profile.Name}' created", command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, "a risk profile");
            return Result.Failure<RiskProfileView>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.RiskProfileCreated(logger, profile.Name);
        return Result.Success(RiskProfileMapping.ToView(profile, 0));
    }
}
