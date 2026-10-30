using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Application.Limits;

/// <summary>A risk-reducing automatic action: a stop tightening, a planned exit or the cancel of the system's own pending order, with its plain-language text.</summary>
public sealed record AutoReduce(Guid AccountId, Guid IntentPublicId, string Kind, string Text, long? SettingsId, long? LimitsId);

/// <summary>
/// "Every action notified" (spec P12-09 criterion 11, D9): nothing is handed to the sender without its action-log row. An entry is handed over only after <see cref="ReserveEntry"/> wrote the ledger row and its <c>auto_entry</c> row in one
/// transaction; a stop tightening, an exit or a cancel only after its own row is written. A row that cannot be written means the automatic action is not taken: the stop already rests at the broker and P12-04's watchdog protects the
/// position on its own path. Composed by no host and no hand-over is implemented in this slice.
/// </summary>
public sealed partial class AutoActions(ReserveEntry reserve, IActionLog actions, IEntryHandOver entries, IReduceHandOver reductions, ILogger<AutoActions> logger)
{
    private static readonly string[] ReduceKinds = [ActionKinds.AutoStopTighten, ActionKinds.AutoExit, ActionKinds.AutoCancel];

    public async Task<ReserveOutcome> EntryAsync(AutoEntry entry, EntryFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var outcome = await reserve.ReserveAsync(entry, facts, cancellationToken).ConfigureAwait(false);
        if (outcome is { Status: ReserveStatus.Reserved, LedgerId: { } ledgerId, ActionLogId: not null })
        {
            await entries.HandOverAsync(entry.IntentPublicId, ledgerId, cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    /// <summary>Writes the action's row, then hands it over; null (and nothing handed over) when the row could not be written.</summary>
    public async Task<long?> ReduceAsync(AutoReduce action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!ReduceKinds.Contains(action.Kind))
        {
            throw new ArgumentException("Only a stop tightening, an exit or a cancel is a reducing action.", nameof(action));
        }

        var id = await actions.AppendAsync(new ActionLogEntry(action.AccountId, action.Kind, ActionKinds.ByAuto, action.IntentPublicId, null, action.Text, [], action.SettingsId, action.LimitsId), cancellationToken).ConfigureAwait(false);
        var who = ReserveEntry.Short(action.AccountId);
        if (id is not { } rowId)
        {
            LogNotTaken(logger, who, action.Kind);
            return null;
        }

        await reductions.HandOverAsync(action.IntentPublicId, action.Kind, rowId, cancellationToken).ConfigureAwait(false);
        LogReduced(logger, who, action.Kind);
        return rowId;
    }

    [LoggerMessage(EventId = 6724, Level = LogLevel.Information, Message = "execution.auto_reduce account={Account} kind={Kind}")]
    private static partial void LogReduced(ILogger logger, string account, string kind);

    [LoggerMessage(EventId = 6725, Level = LogLevel.Warning, Message = "execution.auto_reduce_not_taken account={Account} kind={Kind} reason=no_action_row")]
    private static partial void LogNotTaken(ILogger logger, string account, string kind);
}
