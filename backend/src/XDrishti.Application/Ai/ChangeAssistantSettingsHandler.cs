using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>
/// The owner's act of changing the assistant's switches (spec P9-03 AC 8, ADR 0137, 0138). The domain refuses a non-owner actor and out-of-range values before anything is written; a change that raises exposure needs the password the endpoint verified; the store appends one row (never an update) if nobody
/// changed the settings meanwhile. Nothing else changes: not the model, the roles or any other setting.
/// </summary>
internal sealed class ChangeAssistantSettingsHandler(
    IAssistantSettingsStore store,
    IQueryHandler<GetAssistantStatusQuery, AssistantStatusView> status,
    ILogger<ChangeAssistantSettingsHandler> logger) : ICommandHandler<ChangeAssistantSettingsCommand, AssistantStatusView>
{
    public async ValueTask<Result<AssistantStatusView>> HandleAsync(ChangeAssistantSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var current = await store.CurrentAsync(cancellationToken);
        var draftConfirm = command.AllowDraftConfirm ?? current.AllowDraftConfirm;
        var autoQuick = command.AutoLabQuickTest ?? current.AutoLabQuickTest;
        var proposalAccept = command.AllowProposalAccept ?? current.AllowProposalAccept;
        var autoHypothesis = command.AutoAcceptHypothesis ?? current.AutoAcceptHypothesis;
        var change = AssistantSettingsChange.Create(command.Actor, command.Reason, command.Enabled, command.PortfolioContext, command.MaxToolCalls, draftConfirm, autoQuick, proposalAccept, autoHypothesis);
        if (change.IsFailure)
        {
            AssistantLog.Refused(logger, change.Error.Code);
            return Result.Failure<AssistantStatusView>(change.Error);
        }

        if (command.ExpectedSettingsId is { } expected && expected != (current.Id ?? 0))
        {
            return Stale();
        }

        if (AssistantSettingsChange.RaisesExposure(current, command.Enabled, command.PortfolioContext, command.MaxToolCalls, draftConfirm, autoQuick, proposalAccept, autoHypothesis) && !command.PasswordVerified)
        {
            AssistantLog.Refused(logger, AuthErrors.InvalidCredentialsCode);
            return Result.Failure<AssistantStatusView>(AuthErrors.InvalidCredentials);
        }

        var row = await store.AppendAsync(change.Value, current.Id, cancellationToken);
        if (row is null)
        {
            return Stale();
        }

        AssistantLog.SettingsChanged(logger, row.Id ?? 0);
        return Result.Success(await status.HandleAsync(new GetAssistantStatusQuery(), cancellationToken));
    }

    private Result<AssistantStatusView> Stale()
    {
        AssistantLog.Refused(logger, "assistant.stale");
        return Result.Failure<AssistantStatusView>(Error.Conflict("assistant.stale", "The settings were changed meanwhile; read them again."));
    }
}
