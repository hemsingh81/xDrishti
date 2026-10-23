using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Gives a model a role (spec P6-07, ADR 0070): the designation is appended, never changed. The champion rule (<see cref="ChampionRule"/>) is checked here; the verdict of a model is fixed at registration, so there is nothing to race.</summary>
internal sealed class DesignateModelHandler(IModelRegistry registry, TimeProvider time, ILogger<DesignateModelHandler> logger) : ICommandHandler<DesignateModelCommand, DesignationOutcome>
{
    public async ValueTask<Result<DesignationOutcome>> HandleAsync(DesignateModelCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = new List<FieldError>();
        if (!Enum.IsDefined(command.Role))
        {
            problems.Add(new FieldError("role", "The role is champion, challenger or retired."));
        }

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500)
        {
            problems.Add(new FieldError("reason", "The reason is required and at most 500 characters."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        if (problems.Count > 0)
        {
            ModelLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<DesignationOutcome>(LearningErrors.Invalid(problems));
        }

        var model = await registry.FindAsync(command.ModelId, cancellationToken);
        if (model is null)
        {
            ModelLog.Refused(logger, ModelErrors.NotFoundCode);
            return Result.Failure<DesignationOutcome>(ModelErrors.NotFound(command.ModelId));
        }

        if (!ChampionRule.Allows(command.Role, model.Model.Verdict))
        {
            ModelLog.Refused(logger, ModelErrors.NotEligibleCode);
            return Result.Failure<DesignationOutcome>(ModelErrors.NotEligible(command.ModelId, model.Model.Verdict));
        }

        if (model.Role == command.Role)
        {
            return Result.Success(new DesignationOutcome(command.ModelId, command.Role.Code(), false));
        }

        await registry.DesignateAsync(command.ModelId, command.Role, command.Reason.Trim(), command.Actor, time.GetUtcNow(), cancellationToken);
        var code = command.Role.Code();
        ModelLog.Designated(logger, command.ModelId, code);
        return Result.Success(new DesignationOutcome(command.ModelId, code, true));
    }
}
