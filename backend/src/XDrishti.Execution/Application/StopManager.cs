using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Application;

/// <summary>
/// Changes to a resting stop (P12-04): tighten, the break-even move and the stop-triggered-not-filled rule (I6). A stop only tightens (<see cref="StopRules.CheckTighten"/>; a loosening is refused here as well as by <see cref="ActionPolicy"/>),
/// stays two ticks below the last price, and an intent has at most <c>exec.max_modifications_per_intent</c> modifications (counted from its stored <c>modify_sent</c> events). Tightening needs a fresh book and no freeze halt. I6 changes only the limit of
/// a triggered stop, keeping its trigger, once per leg. Only a stop of ours (by client id) can be changed; a super-order leg cannot, and the answer says so.
/// </summary>
public sealed class StopManager(IProtectionSource source, IBrokerStops stops, ProtectiveGuard guard, IExecEventStore events, ProtectionReporter report, Func<ProtectionSettings?> settings, TimeProvider clock)
{
    private readonly Dictionary<long, DateTimeOffset> _beyondSince = [];
    private readonly HashSet<string> _repriced = [];

    /// <summary>Returns "ok" or the reason nothing was sent.</summary>
    public async Task<string> TightenAsync(Guid accountId, long intentId, decimal newTrigger, CancellationToken cancellationToken)
    {
        if (ActionPolicy.Check(ExecAction.StopTighten, ExecMode.SemiAuto) == ActionVerdict.Refused)
        {
            return "policy_refused";
        }

        if (await ContextAsync(accountId, intentId, cancellationToken).ConfigureAwait(false) is not { } c)
        {
            return "unavailable";
        }

        if (c.Refusal is { } refusal)
        {
            return refusal;
        }

        if (c.Snapshot.Halt == HaltLevel.Freeze)
        {
            return "frozen";
        }

        var count = (await events.EventsAsync(intentId, cancellationToken).ConfigureAwait(false)).Count(e => e.Kind == ExecEventKind.ModifySent);
        if (StopRules.CheckTighten(c.Order.Trigger ?? c.Order.Price, newTrigger, c.Position.Last, count, c.Settings.MaxModifications) is { } reason)
        {
            return reason;
        }

        return await ModifyAsync(c, newTrigger, StopRules.StopLimit(newTrigger, c.Settings.StopLimitBufferPct), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves the stop to entry plus one tick once the last price is <c>exec.breakeven_at_r</c> initial risks in favour; with the setting 0 it does nothing.</summary>
    public async Task<string> BreakevenAsync(Guid accountId, long intentId, CancellationToken cancellationToken)
    {
        if (settings() is not { BreakevenAtR: > 0m } s)
        {
            return "off";
        }

        var snapshot = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        var position = snapshot?.Positions.FirstOrDefault(p => p.IntentId == intentId && p.AccountId == accountId);
        return position?.Last is not { } last || StopRules.BreakevenTrigger(position.IntentEntry, position.IntentStop, last, s.BreakevenAtR) is not { } target
            ? "not_reached"
            : await TightenAsync(accountId, intentId, target, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>I6: the last price has been through the stop's limit for <c>exec.stop_unfilled_s</c> and the leg is still open; its limit is modified to the marketable price (last less the exit collar), the trigger kept, one change per leg.</summary>
    public async Task<string> StopUnfilledAsync(Guid accountId, long intentId, CancellationToken cancellationToken)
    {
        if (await ContextAsync(accountId, intentId, cancellationToken).ConfigureAwait(false) is not { } c)
        {
            return "unavailable";
        }

        if (c.Refusal is { } refusal)
        {
            return refusal;
        }

        var now = clock.GetUtcNow();
        if (c.Position.Last is not { } last || last > c.Order.Price)
        {
            _beyondSince.Remove(intentId);
            return "not_beyond";
        }

        var since = _beyondSince.TryGetValue(intentId, out var first) ? first : _beyondSince[intentId] = now;
        if (now - since < c.Settings.StopUnfilled)
        {
            return "waiting";
        }

        await report.RaiseAsync(AlarmCodes.StopTriggeredUnfilled, accountId, intentId, "the stop is through its limit and still open", cancellationToken).ConfigureAwait(false);
        var key = c.Order.ClientOrderId ?? c.Order.OrderId;
        var limit = StopRules.MarketableSell(last, c.Settings.ExitCollarPct);
        if (_repriced.Contains(key) || limit >= c.Order.Price)
        {
            return "already_repriced";
        }

        if (c.Snapshot.Halt == HaltLevel.Freeze && !ProtectionExitPolicy.FreezeAgrees(c.Position, now))
        {
            return "freeze_needs_position_read";
        }

        var result = await ModifyAsync(c, c.Order.Trigger ?? c.Order.Price, limit, cancellationToken, countAsModification: false).ConfigureAwait(false);
        if (result == "ok")
        {
            _repriced.Add(key);
        }

        return result;
    }

    private sealed record Context(ProtectionSnapshot Snapshot, ProtectedPosition Position, BrokerOrderView Order, ProtectionSettings Settings, string? Refusal);

    private async Task<Context?> ContextAsync(Guid accountId, long intentId, CancellationToken cancellationToken)
    {
        if (settings() is not { } s || await source.ReadAsync(cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            return null;
        }

        var position = snapshot.Positions.FirstOrDefault(p => p.IntentId == intentId && p.AccountId == accountId);
        if (position is null || position.NetQty <= 0)
        {
            return null;
        }

        var order = snapshot.OrderBook.FirstOrDefault(o => o.SecurityId == position.SecurityId && o.Status == ProtectionMonitor.StatusPending && ProtectionBook.IsStop(o));
        var stopCount = snapshot.OrderBook.Count(o => o.SecurityId == position.SecurityId && o.Status == ProtectionMonitor.StatusPending && ProtectionBook.IsStop(o));
        if (order is null)
        {
            return null;
        }

        string? refusal = null;
        if (!(await guard.OpenAsync(accountId, cancellationToken).ConfigureAwait(false)).Open)
        {
            refusal = "gate_closed";
        }
        else if (snapshot.BookReadAt is not { } at || clock.GetUtcNow() - at > s.BlindAfter)
        {
            refusal = "book_stale";
        }
        else if (stopCount != 1)
        {
            refusal = "ambiguous_stop";
        }
        else if (order.ClientOrderId is not { } cid || !ClientOrderId.TryParse(cid, out var id) || id.Kind != LegKind.Stop)
        {
            refusal = "not_our_stop";
        }

        return new Context(snapshot, position, order, s, refusal);
    }

    private async Task<string> ModifyAsync(Context c, decimal trigger, decimal limit, CancellationToken cancellationToken, bool countAsModification = true)
    {
        var ack = await stops.ModifyStopAsync(c.Order.ClientOrderId!, trigger, limit, cancellationToken).ConfigureAwait(false);
        if (ack.Outcome == BrokerOutcome.Unknown)
        {
            await report.RaiseAsync(AlarmCodes.SendUnknown, c.Position.AccountId, c.Position.IntentId, "a stop change has an unknown outcome", cancellationToken).ConfigureAwait(false);
            return "unknown";
        }

        if (ack.Outcome != BrokerOutcome.Acknowledged)
        {
            return ack.Reason ?? "not_sent";
        }

        if (await events.StateAsync(c.Position.IntentId, cancellationToken).ConfigureAwait(false) is { } state)
        {
            await events.AppendAsync(c.Position.IntentId, new ExecEvent(state.Seq + 1, ExecEventKind.ModifySent, null, null, ExecEventSource.Engine, ProtectionReporter.Actor, clock.GetUtcNow(), 0, countAsModification ? "{\"what\":\"stop\"}" : "{\"what\":\"limit\"}"), cancellationToken).ConfigureAwait(false);
        }

        return "ok";
    }
}
