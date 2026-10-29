using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application;

/// <summary>
/// Turns a protection finding into its records (P12-04 criteria 11 and 12): one alarm row (once while it stays open), the event <c>alarm</c> on the intent when the alarm is new, and, when asked, a halt request (made first, so a failing event store cannot stop it). The alarm row is the record that
/// counts; a failed event append (a final intent, a conflict) never hides the alarm. The payload carries only the code.
/// </summary>
public sealed class ProtectionReporter(IAlarmStore alarms, IHaltRequests halts, IExecEventStore? events, TimeProvider clock)
{
    public const string Actor = "system:protection";

    // halt: the level this finding asks for, if any. haltInForce: the level in force now (null: none). The request is made when the alarm is new, or when none (or a milder one) is in force,
    // so a request that was lost or not acted on is made again while the alarm stands.
    public async Task<bool> RaiseAsync(string code, Guid? accountId, long? intentId, string detail, CancellationToken cancellationToken, HaltLevel? halt = null, HaltLevel? haltInForce = null)
    {
        var now = clock.GetUtcNow();
        var created = await alarms.RaiseAsync(new AlarmDraft(code, accountId, intentId, detail.Length > 300 ? detail[..300] : detail, now), cancellationToken).ConfigureAwait(false);

        // The halt request comes before the event append: a failing event store must never keep the request from being made.
        if (halt is { } level && (created || haltInForce is null || haltInForce < level))
        {
            await halts.RequestAsync(accountId, level, code, detail, cancellationToken).ConfigureAwait(false);
        }

        if (created && intentId is { } id && events is not null)
        {
            try
            {
                if (await events.StateAsync(id, cancellationToken).ConfigureAwait(false) is { } state)
                {
                    await events.AppendAsync(id, new ExecEvent(state.Seq + 1, ExecEventKind.Alarm, null, null, ExecEventSource.Engine, Actor, now, 0, $"{{\"code\":\"{code}\"}}"), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The alarm row is the record that counts; the event is a convenience for P12-07.
            }
        }

        return created;
    }

    public Task<int> ClearAsync(string code, Guid? accountId, long? intentId, CancellationToken cancellationToken) =>
        alarms.ClearAsync(code, accountId, intentId, Actor, AlarmClearHow.Auto, clock.GetUtcNow(), cancellationToken);

    /// <summary>Moves the stored intent along a legal move; a refused or failed move changes nothing and is reported as false.</summary>
    public async Task<bool> MoveAsync(long intentId, ExecState to, CancellationToken cancellationToken)
    {
        if (events is null)
        {
            return false;
        }

        try
        {
            if (await events.StateAsync(intentId, cancellationToken).ConfigureAwait(false) is not { } state || state.State == to)
            {
                return false;
            }

            var result = await events.AppendAsync(intentId, ExecEvent.Move(state.Seq + 1, state.State, to, ExecEventSource.Engine, Actor, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            return result.IsSuccess;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }
}
