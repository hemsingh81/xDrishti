using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Stores the pre-registration of a strategy version once (spec CF-06, D1): validates the five parts, refuses a second one for the version (the record is frozen) and one written after the plan was frozen by a full run (it would not be before the search) and logs only ids, never a text.</summary>
internal sealed class RegisterStrategyHandler(IStrategyStore strategies, IRegistrationStore store, IPlanFreezeStore freezes, TimeProvider time, ILogger<RegisterStrategyHandler> logger) : ICommandHandler<RegisterStrategyCommand, RegistrationView>
{
    public async ValueTask<Result<RegistrationView>> HandleAsync(RegisterStrategyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Actor))
        {
            return Result.Failure<RegistrationView>(Error.Forbidden("lab.no_owner", "A registration is written by the owner."));
        }

        if (await strategies.GetVersionAsync(command.StrategyId, command.Version, cancellationToken) is null)
        {
            return Result.Failure<RegistrationView>(LabErrors.NotFound(command.StrategyId, command.Version));
        }

        if (await freezes.FirstAsync(command.StrategyId, command.Version, cancellationToken) is not null)
        {
            return Result.Failure<RegistrationView>(LabErrors.RegistrationAfterFreeze(command.StrategyId, command.Version));
        }

        var made = Registration.Create(command.Counterparty, command.ParameterSigns, command.Applicability, command.ExpectedEdge, command.Falsifier);
        if (made.IsFailure)
        {
            return Result.Failure<RegistrationView>(made.Error);
        }

        var stored = new StoredRegistration(command.StrategyId, command.Version, made.Value, made.Value.TextHash(), time.GetUtcNow(), command.Actor);
        if (!await store.InsertAsync(stored, cancellationToken))
        {
            return Result.Failure<RegistrationView>(LabErrors.RegistrationFrozen(command.StrategyId, command.Version));
        }

        LabRunLog.Registered(logger, command.StrategyId, command.Version);
        return Result.Success(ViewOf(stored));
    }

    internal static RegistrationView ViewOf(StoredRegistration s) =>
        new(s.StrategyId, s.Version, s.Registration.Counterparty, s.Registration.ParameterSigns, s.Registration.Applicability, s.Registration.ExpectedEdge, s.Registration.Falsifier, s.TextHash, s.CreatedAt, s.Actor);
}

/// <summary>Reads the registration of a version.</summary>
internal sealed class GetRegistrationHandler(IRegistrationStore store) : IQueryHandler<GetRegistrationQuery, Result<RegistrationView>>
{
    public async ValueTask<Result<RegistrationView>> HandleAsync(GetRegistrationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var stored = await store.GetAsync(query.StrategyId, query.Version, cancellationToken);
        return stored is null
            ? Result.Failure<RegistrationView>(LabErrors.NotFound(query.StrategyId, query.Version))
            : Result.Success(RegisterStrategyHandler.ViewOf(stored));
    }
}
