using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Paper;

/// <summary>
/// The owner restarts the count (spec P10-05 criterion 6): an <c>owner_restart</c> epoch effective the next trading day. Always allowed with a reason; it deletes and edits nothing, the earlier epoch keeps its trades. CLI only: no HTTP write exists.
/// </summary>
internal sealed class StartEpochHandler(IPaperStore paper, IGovernanceStore store, ITradingCalendar calendars, TimeProvider time, ILogger<StartEpochHandler> logger) : ICommandHandler<StartEpochCommand, EpochView>
{
    public const int MinReason = 10;

    public const int MaxReason = 300;

    public async ValueTask<Result<EpochView>> HandleAsync(StartEpochCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < MinReason or > MaxReason)
        {
            return Result.Failure<EpochView>(PaperErrors.Invalid("reason", $"A reason of {MinReason} to {MaxReason} characters is required."));
        }

        var newest = await paper.CurrentEpochAsync(cancellationToken);
        if (newest is null)
        {
            return Result.Failure<EpochView>(PaperErrors.Invalid("epoch", "No epoch exists yet; the first starts with the first capture."));
        }

        var now = time.GetUtcNow();
        var calendar = await calendars.GetAsync(cancellationToken);
        var start = IndiaTime.Today(now).AddDays(1);
        for (var i = 0; i < 15 && !calendar.IsTradingDay(start); i++)
        {
            start = start.AddDays(1);
        }

        if (newest.StartedOn >= start)
        {
            return Result.Failure<EpochView>(PaperErrors.Invalid("date", $"The newest epoch already starts on {start:yyyy-MM-dd}."));
        }

        var hash = (await store.LatestSnapshotAsync(true, cancellationToken))?.Hash;
        var row = await store.StartOwnerEpochAsync(new NewOwnerEpoch(start, now, hash, reason, command.Actor, newest.Id), cancellationToken);
        if (row is null)
        {
            return Result.Failure<EpochView>(Error.Conflict("paper.epoch_changed", "Another epoch started meanwhile; show the epochs and try again."));
        }

        GovernanceLog.Restart(logger, command.Actor, start);
        return Result.Success(new EpochView(row.Id, row.Kind, row.StartedOn, null, hash is null ? null : GovernanceWording.Short(hash), 0, 0, 0, 0, row.Reason));
    }
}

/// <summary>The owner explains a recorded change with a note and optionally an accepted hypothesis (spec P10-05 criterion 8). It stores a note; it applies and changes nothing.</summary>
internal sealed class ExplainChangeHandler(IGovernanceStore store, IHypothesisLog hypotheses, TimeProvider time, ILogger<ExplainChangeHandler> logger) : ICommandHandler<ExplainChangeCommand, ExplainOutcome>
{
    public async ValueTask<Result<ExplainOutcome>> HandleAsync(ExplainChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < StartEpochHandler.MinReason or > StartEpochHandler.MaxReason)
        {
            return Result.Failure<ExplainOutcome>(PaperErrors.Invalid("reason", $"A reason of {StartEpochHandler.MinReason} to {StartEpochHandler.MaxReason} characters is required."));
        }

        if (await store.FindChangeAsync(command.ChangeId, cancellationToken) is not { } change)
        {
            return Result.Failure<ExplainOutcome>(Error.NotFound(PaperErrors.NotFoundCode, "No recorded change " + command.ChangeId + "."));
        }

        if (command.HypothesisId is { } id)
        {
            var history = await hypotheses.HistoryAsync(id, cancellationToken);
            if (history.Count == 0)
            {
                return Result.Failure<ExplainOutcome>(Error.NotFound(PaperErrors.NotFoundCode, "No hypothesis " + id + "."));
            }

            if (history[^1].Row.State != "accepted")
            {
                return Result.Failure<ExplainOutcome>(PaperErrors.Invalid("hypothesis", "Only an accepted hypothesis can be attached; hypothesis " + id + " is " + history[^1].Row.State + "."));
            }
        }

        await store.AddNoteAsync(change.EpochId, change.Id, command.HypothesisId, reason, command.Actor, time.GetUtcNow(), cancellationToken);
        GovernanceLog.Note(logger, change.EpochId, command.Actor);
        return Result.Success(new ExplainOutcome(change.Id, change.EpochId));
    }
}
