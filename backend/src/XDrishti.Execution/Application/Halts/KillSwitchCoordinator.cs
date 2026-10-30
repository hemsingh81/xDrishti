using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application.Halts;

/// <summary>
/// The broker's own kill switch (UNVERIFIED, P12-05 Decision 1). It can be switched on only when every position is closed and no order is pending, and it then disables trading for the day. No host implements this port and none registers it; the
/// tests use a fake. An implementation, if one is ever built, must answer for itself and must not be reachable from a refused environment.
/// </summary>
public interface IBrokerKillSwitch
{
    Task<BrokerAck> ActivateAsync(CancellationToken cancellationToken);
}

/// <summary>Insert-only record of what the coordinator did for a halt: <c>deferred</c> (once), <c>activated</c> or <c>failed</c>.</summary>
public interface IKillSwitchLog
{
    Task AppendAsync(long haltId, string outcome, string detail, DateTimeOffset at, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> OutcomesAsync(long haltId, CancellationToken cancellationToken);
}

/// <summary>An in-memory log for tests.</summary>
public sealed class InMemoryKillSwitchLog : IKillSwitchLog
{
    private readonly Lock _gate = new();
    private readonly List<(long HaltId, string Outcome)> _rows = [];

    public Task AppendAsync(long haltId, string outcome, string detail, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _rows.Add((haltId, outcome));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> OutcomesAsync(long haltId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<string>>([.. _rows.Where(r => r.HaltId == haltId).Select(r => r.Outcome)]);
        }
    }
}

/// <summary>
/// Calls the broker's switch for a halt only when ALL of: the setting <c>exec.broker_kill_switch_on_halt</c> or the halt's own choice is on, the trigger is one that only the owner can clear (never an automatic-clearing one: the switch locks the owner's own trading for
/// the day and would outlive the halt), the book is readable and shows no position, no working order and no pending order at all. Otherwise it records <c>deferred</c> once and tries again when flat. The call is made at most once per halt: a failed or unknown answer is recorded and never repeated here. An unreadable book is not flat.
/// </summary>
public sealed class KillSwitchCoordinator(IBrokerKillSwitch? broker, IKillSwitchLog log, IProtectionSource source, Func<bool> settingOn, TimeProvider clock)
{
    private readonly HashSet<long> _done = [];

    public async Task TickAsync(IReadOnlyList<ActiveHalt> halts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(halts);
        foreach (var halt in halts.Where(h => !_done.Contains(h.Id) && (settingOn() || h.BrokerKillSwitch) && !HaltTriggers.IsAutoClearing(h.TriggerCode)))
        {
            if (broker is null)
            {
                continue;
            }

            var outcomes = await log.OutcomesAsync(halt.Id, cancellationToken).ConfigureAwait(false);
            if (outcomes.Contains("activated") || outcomes.Contains("failed"))
            {
                _done.Add(halt.Id);
                continue;
            }

            var now = clock.GetUtcNow();
            var snapshot = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null || snapshot.BookReadAt is null || snapshot.Positions.Any(p => p.NetQty != 0) || snapshot.Entries.Count > 0 || snapshot.OrderBook.Any(o => o.Status is "TRANSIT" or "PENDING" or "PART_TRADED"))
            {
                if (!outcomes.Contains("deferred"))
                {
                    await log.AppendAsync(halt.Id, "deferred", "broker_kill_switch_deferred", now, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            BrokerAck ack;
            try
            {
                ack = await broker.ActivateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The request may have left: recorded as failed with no content, and never repeated.
                ack = new BrokerAck(BrokerOutcome.Unknown, null, "exception");
            }

            await log.AppendAsync(halt.Id, ack.Outcome == BrokerOutcome.Acknowledged ? "activated" : "failed", ack.Reason ?? ack.Outcome.ToString(), now, CancellationToken.None).ConfigureAwait(false);

            // Never repeated on its own: an Unknown answer may have activated it, and a refusal is for the owner to read.
            _done.Add(halt.Id);
        }
    }
}
