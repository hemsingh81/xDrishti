using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>
/// The owner's confirm (spec P9-07 AC 7, 8, D1, D6). With Discard and the auto handler of a later slice, this is the only type of the drafting code that stores a strategy or starts a Lab run. It refuses any actor that is not the signed-in owner and the switch
/// <c>allow_draft_confirm</c> while it is off; the endpoint has already asked for the fresh password. It re-validates the text actually sent, never the stored draft text, stores it as a normal library version with a note, starts a quick test only when asked and only on the universe the
/// owner picked, and writes the one decision row. A second confirm of the same draft returns the first result and starts nothing.
/// </summary>
internal sealed class ConfirmDraftHandler(
    IDraftStore store,
    IAssistantSettingsStore settings,
    IStrategyChecker checker,
    IStrategyStore strategies,
    ICommandHandler<CreateStrategyCommand, CreatedStrategy> create,
    ICommandHandler<StartLabRunCommand, StartedLabRun> start,
    ILabRunStore runs,
    ILogger<ConfirmDraftHandler> logger) : ICommandHandler<ConfirmDraftCommand, DraftView>
{
    public async ValueTask<Result<DraftView>> HandleAsync(ConfirmDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Refuse(DraftErrors.OwnerOnly());
        }

        var current = await settings.CurrentAsync(cancellationToken);
        if (!current.AllowDraftConfirm)
        {
            return Refuse(DraftErrors.CapabilityOff());
        }

        var draft = await store.GetAsync(command.Id, cancellationToken);
        if (draft is null)
        {
            return Refuse(DraftErrors.NotFound(command.Id));
        }

        await using var confirmLock = await store.LockConfirmAsync(cancellationToken);
        draft = await store.GetAsync(command.Id, cancellationToken) ?? draft;
        var earlier = await store.DecisionAsync(draft.Id, cancellationToken);
        if (earlier is not null)
        {
            return earlier.Decision == "confirmed" ? Result.Success(await GetDraftHandler.ViewAsync(draft, store, runs, cancellationToken)) : Refuse(DraftErrors.Discarded());
        }

        if (draft.Status is not (DraftStatus.Valid or DraftStatus.Invalid))
        {
            return Refuse(DraftErrors.DraftInvalid());
        }

        var edited = command.Text is not null && !Same(command.Text, draft.Yaml);
        var text = command.Text ?? draft.Yaml;
        if (string.IsNullOrWhiteSpace(text) || (draft.Status == DraftStatus.Invalid && !edited))
        {
            return Refuse(DraftErrors.DraftInvalid());
        }

        if (Encoding.UTF8.GetByteCount(text) > LabErrors.MaxTextBytes)
        {
            return Refuse(LabErrors.InvalidStrategy([LabErrors.TooLarge()]));
        }

        var check = checker.Check(text);
        if (check.Description is not { } description)
        {
            return Refuse(LabErrors.InvalidStrategy(check.Issues));
        }

        if (command.Run && (command.BasketId is null) == (command.Symbols is null))
        {
            return Refuse(DraftErrors.Invalid("Pick exactly one of a basket and a list of symbols for the quick test."));
        }

        if (await strategies.GetAsync(description.Id, cancellationToken) is not null)
        {
            return Refuse(DraftErrors.IdTaken(description.Id));
        }

        var created = await create.HandleAsync(new CreateStrategyCommand(text, $"Assistant draft {draft.Id}; confirmed by owner"), cancellationToken);
        if (created.IsFailure)
        {
            return Refuse(created.Error);
        }

        Guid? runId = null;
        string? runError = null;
        if (command.Run)
        {
            var started = await start.HandleAsync(new StartLabRunCommand(created.Value.Id, created.Value.Version, command.BasketId, command.Symbols, "quick", null, null), cancellationToken);
            runId = started.IsSuccess ? started.Value.Run.Id : null;
            runError = started.IsSuccess ? null : started.Error.Code;
        }

        var written = await DecisionWriter.WriteAsync(store, logger, new NewDraftDecision(draft.Id, "confirmed", created.Value.Id, created.Value.Version, runId, edited, false, null, null, command.Actor, runError));
        DraftLog.Confirmed(logger, draft.Id, created.Value.Id, created.Value.Version, written.Decision.LabRunId, edited);
        return Result.Success(await GetDraftHandler.ViewAsync(draft, store, runs, CancellationToken.None));
    }

    private static bool Same(string a, string? b) => string.Equals(a.ReplaceLineEndings("\n").TrimEnd(), b?.ReplaceLineEndings("\n").TrimEnd(), StringComparison.Ordinal);

    private Result<DraftView> Refuse(Error error)
    {
        DraftLog.Refused(logger, error.Code);
        return Result.Failure<DraftView>(error);
    }
}

/// <summary>The owner discards a draft (spec P9-07): one decision row; nothing is stored as a strategy. Free of the password (the safe direction); a confirmed draft cannot be discarded.</summary>
internal sealed class DiscardDraftHandler(IDraftStore store, ILabRunStore runs, ILogger<DiscardDraftHandler> logger) : ICommandHandler<DiscardDraftCommand, DraftView>
{
    public async ValueTask<Result<DraftView>> HandleAsync(DiscardDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Result.Failure<DraftView>(DraftErrors.OwnerOnly());
        }

        var draft = await store.GetAsync(command.Id, cancellationToken);
        if (draft is null)
        {
            return Result.Failure<DraftView>(DraftErrors.NotFound(command.Id));
        }

        var written = await store.InsertDecisionAsync(new NewDraftDecision(draft.Id, "discarded", null, null, null, false, false, null, null, command.Actor), cancellationToken);
        if (written.Decision.Decision == "confirmed")
        {
            return Result.Failure<DraftView>(DraftErrors.Confirmed());
        }

        if (written.Created)
        {
            DraftLog.Discarded(logger, draft.Id);
        }

        return Result.Success(await GetDraftHandler.ViewAsync(draft, store, runs, cancellationToken));
    }
}
