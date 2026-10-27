using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>
/// Starts a drafting job (spec P9-07 AC 10): the owner only, drafting and the assistant both on, an idea of 10 to 2,000 plain characters, not over the day's limit, the model slot free. Answers at once with the reserved id; the job runs on its own scope.
/// The idea is stored by the server exactly as the owner posted it.
/// </summary>
internal sealed class StartDraftHandler(
    IAssistantSettingsStore settings,
    IDraftStore store,
    DraftJobs jobs,
    TurnGate gate,
    DraftOptions options,
    TimeProvider time,
    ILogger<StartDraftHandler> logger) : ICommandHandler<StartDraftCommand, DraftView>
{
    public async ValueTask<Result<DraftView>> HandleAsync(StartDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Refuse(DraftErrors.OwnerOnly());
        }

        var idea = command.Idea ?? string.Empty;
        if (idea.Trim().Length is < DraftErrors.MinIdea or > DraftErrors.MaxIdea || idea.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
        {
            return Refuse(DraftErrors.Invalid($"The idea is {DraftErrors.MinIdea} to {DraftErrors.MaxIdea} characters of plain text, without control characters."));
        }

        if (!options.Enabled || !(await settings.CurrentAsync(cancellationToken)).Enabled)
        {
            return Refuse(DraftErrors.Disabled());
        }

        var start = new DateTimeOffset(IndiaTime.Today(time.GetUtcNow()).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        if (await store.CountBetweenAsync(start, start.AddDays(1), cancellationToken) + jobs.Count >= options.MaxPerDay)
        {
            return Refuse(DraftErrors.Limit(options.MaxPerDay));
        }

        var slot = gate.TryEnter();
        if (slot is null)
        {
            return Refuse(DraftErrors.Busy());
        }

        long id;
        try
        {
            id = await store.ReserveIdAsync(cancellationToken);
        }
#pragma warning disable CA1031 // the slot must be given back whatever failed
        catch (Exception)
#pragma warning restore CA1031
        {
            slot.Dispose();
            throw;
        }

        var job = new DraftStrategyCommand(id, idea.Trim(), command.BasketId, command.Actor);
        DraftLog.Started(logger, id);
        jobs.Launch(id, job.Idea, slot, async services => await services.GetRequiredService<ICommandHandler<DraftStrategyCommand, DraftView>>().HandleAsync(job, CancellationToken.None), failed => DraftLog.JobCrashed(logger, failed));
        return Result.Success(DraftView.InProgress(id, job.Idea));
    }

    private Result<DraftView> Refuse(Error error)
    {
        DraftLog.Refused(logger, error.Code);
        return Result.Failure<DraftView>(error);
    }
}
