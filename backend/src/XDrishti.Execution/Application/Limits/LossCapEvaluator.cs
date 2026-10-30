using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Application.Limits;

/// <summary>The loss cap of the sheet in force, for P12-05's <c>loss_cap</c> trigger: none when the sheet is incomplete, invalid or unreadable.</summary>
public sealed class LimitsLossCapSource(ILimitsStore limits) : ILossCapSource
{
    public async Task<LossCapSetting?> ForAsync(Guid accountId, CancellationToken cancellationToken)
    {
        try
        {
            var row = (await limits.ReadAsync(accountId, cancellationToken).ConfigureAwait(false)).InForce;
            return row is { Limits: { IsComplete: true, MaxLossPerDay: { } cap } sheet } && sheet.Invalid() is null ? new LossCapSetting(cap, sheet.ExitAllOnLossHalt, row.Id) : null;
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            return null;
        }
    }
}

/// <summary>
/// The day halt of the loss cap (spec P12-09 criterion 4, D1): on each position or price update, and at least every 15 s while positions are open, the caller passes the day's realised and unrealised result. At or below minus the cap it asks
/// P12-05 for a <c>loss_cap</c> halt (level <c>entries</c>, or <c>exit_all</c> when the sheet says so; P12-05 decides), once per account and session, and writes a <c>loss_cap_halt</c> row. An unreadable mark does not halt: the guard
/// refuses new entries (<c>cap.loss_unavailable</c>) and one <c>loss_cap_notice</c> row is written when the mark becomes unreadable. Stops stay at the broker. Composed by no host.
/// </summary>
public sealed partial class LossCapEvaluator(ILossCapSource caps, IHaltRequests halts, IActionLog actions, ILogger<LossCapEvaluator> logger)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid, DateOnly), LossHaltVerdict> _last = [];

    /// <summary>The verdict; null when the account has no complete sheet (no number, no cap halt).</summary>
    public async Task<LossHaltVerdict?> EvaluateAsync(Guid accountId, DateOnly session, decimal? realisedToday, decimal? unrealised, CancellationToken cancellationToken)
    {
        if (await caps.ForAsync(accountId, cancellationToken).ConfigureAwait(false) is not { } cap)
        {
            return null;
        }

        var verdict = LossCap.Halt(realisedToday, unrealised, cap.MaxLossPerDay);
        LossHaltVerdict? previous;
        lock (_gate)
        {
            previous = _last.TryGetValue((accountId, session), out var p) ? p : null;
            if (previous != LossHaltVerdict.Halt && verdict != LossHaltVerdict.Halt)
            {
                _last[(accountId, session)] = verdict;
            }
        }

        if (verdict == LossHaltVerdict.Halt && previous != LossHaltVerdict.Halt)
        {
            var level = cap.ExitAll ? HaltLevel.ExitAll : HaltLevel.Entries;
            await halts.RequestAsync(accountId, level, HaltTriggers.LossCap, "the day's loss reached the cap of the limits sheet", cancellationToken).ConfigureAwait(false);

            // Latch only after the request succeeded: a failed request is retried on the next call. The latch is lost on restart, so P12-05 must treat a repeated loss_cap request as idempotent.
            lock (_gate)
            {
                _last[(accountId, session)] = LossHaltVerdict.Halt;
            }

            var text = cap.ExitAll ? "Day loss cap reached: no new entries today and exit all was asked for." : "Day loss cap reached: no new entries today; the stops stay at the broker.";
            await actions.AppendAsync(new ActionLogEntry(accountId, ActionKinds.LossCapHalt, ActionKinds.BySystem, null, null, text, [], null, cap.LimitsId), cancellationToken).ConfigureAwait(false);
            var (who, name) = (ReserveEntry.Short(accountId), level == HaltLevel.ExitAll ? "exit_all" : "entries");
            LogHalt(logger, who, name);
        }
        else if (verdict == LossHaltVerdict.Unavailable && previous is not (LossHaltVerdict.Unavailable or LossHaltVerdict.Halt))
        {
            await actions.AppendAsync(
                new ActionLogEntry(accountId, ActionKinds.LossCapNotice, ActionKinds.BySystem, null, null, "The day's result cannot be marked: no new entries until it can; the stops stay at the broker.", [AutoLiveCodes.LossUnavailable], null, cap.LimitsId),
                cancellationToken).ConfigureAwait(false);
            var who = ReserveEntry.Short(accountId);
            LogUnavailable(logger, who);
        }

        return verdict;
    }

    [LoggerMessage(EventId = 6726, Level = LogLevel.Warning, Message = "execution.loss_cap_halt account={Account} level={Level}")]
    private static partial void LogHalt(ILogger logger, string account, string level);

    [LoggerMessage(EventId = 6727, Level = LogLevel.Warning, Message = "execution.loss_cap_unavailable account={Account}")]
    private static partial void LogUnavailable(ILogger logger, string account);
}
