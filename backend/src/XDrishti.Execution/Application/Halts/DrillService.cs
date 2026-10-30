using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Halts;

/// <summary>What one drill proved and when.</summary>
public sealed record DrillResult(Guid AccountId, bool Passed, int LatencyMs, string Detail, DateTimeOffset At);

/// <summary>Insert-only record of the drills.</summary>
public interface IDrillStore
{
    Task AppendAsync(DrillResult result, CancellationToken cancellationToken);
}

/// <summary>An in-memory drill record for tests.</summary>
public sealed class InMemoryDrillStore : IDrillStore
{
    private readonly Lock _gate = new();
    private readonly List<DrillResult> _rows = [];

    public IReadOnlyList<DrillResult> Rows
    {
        get
        {
            lock (_gate)
            {
                return [.. _rows];
            }
        }
    }

    public Task AppendAsync(DrillResult result, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _rows.Add(result);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The transport of a drill: it refuses everything and counts the calls. It is built by <see cref="DrillService"/> for its own sender only; no setting, command or host option selects it, and it is not registered anywhere. A drill that works leaves
/// <see cref="Calls"/> at zero.
/// </summary>
public sealed class RefusingBrokerOrders : IBrokerOrders
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken) => Refuse();

    public Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken) => Refuse();

    public Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken) => Refuse();

    public Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult<BrokerLeg?>(null);
    }

    private Task<BrokerAck> Refuse()
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new BrokerAck(BrokerOutcome.NotSent, null, "drill_transport_refuses"));
    }
}

/// <summary>
/// The kill-switch drill (spec P12-05 criterion 10): it proves that a halt stops an entry at the real send path, in how long, and that a protective instruction would still pass, without sending anything. Preconditions: the account holds no position and no
/// working or pending order, and no other halt of any kind is active (otherwise it refuses and changes nothing). It raises a <c>cancel</c> halt with trigger <c>drill</c>, sends one entry through a <see cref="LegSender"/> built here with a <see cref="RefusingBrokerOrders"/>
/// transport (the gate must stop it before the transport is touched), checks the permission table, clears the drill's own halt automatically and stores the row. The drill's halt is the only thing this class ever starts, and the only thing it ever clears.
/// </summary>
public sealed class DrillService(HaltService halts, IHaltStore store, IProtectionSource source, IDrillStore drills, TimeProvider clock)
{
    public const string Actor = "system:drill";

    public async Task<DrillResult> RunAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (await RefusalAsync(accountId, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            return new DrillResult(accountId, false, 0, "refused:" + refusal, clock.GetUtcNow());
        }

        var started = clock.GetTimestamp();
        var raised = (await store.AppendAsync(new HaltDraft(HaltScope.Account, accountId, HaltLevel.Cancel, HaltTriggers.Drill, "system", Actor, "kill-switch drill", clock.GetUtcNow()), cancellationToken).ConfigureAwait(false)).Halt;
        var seen = await halts.ReadAsync(cancellationToken).ConfigureAwait(false) == HaltReading.Halted;
        var latency = (int)Math.Min(int.MaxValue, clock.GetElapsedTime(started).TotalMilliseconds);

        var transport = new RefusingBrokerOrders();
        var stopped = false;
        var table = false;
        var cleared = false;
        try
        {
            var sender = new LegSender(transport, new DrillMode(), halts, new ClosedPreSendCheck(), new NoJournal(), new ExecRateLimiter(clock, 5, 5, 5), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(5));
            var entry = new BrokerLeg("DRILL0000000000", LegKind.Entry, ExecSide.Buy, "0", 1, 1m);
            var attempt = await sender.SendAsync(new SendRequest(accountId, 0, entry, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            stopped = attempt is { Status: SendStatus.Refused, Reason: "halted" } && transport.Calls == 0;
            table = !HaltRules.Permits(HaltLevel.Cancel, HaltAction.NewEntry) && HaltRules.Permits(HaltLevel.Cancel, HaltAction.PlaceOrRepairStop);
        }
        finally
        {
            // The drill's own halt is cleared by the drill's own legitimate path even when something above threw.
            cleared = await store.AutoClearAsync(raised.Id, HaltTriggers.Drill, "drill finished", clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
        }

        var passed = seen && stopped && table && cleared;
        var result = new DrillResult(accountId, passed, latency, $"seen={seen};entry_stopped={stopped};table={table};cleared={cleared};transport_calls={transport.Calls}", clock.GetUtcNow());
        await drills.AppendAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<string?> RefusalAsync(Guid accountId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ActiveHalt> active;
        try
        {
            active = await store.ActiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return "halts_unreadable";
        }

        if (active.Count > 0)
        {
            return "halt_active";
        }

        if (await source.ReadAsync(cancellationToken).ConfigureAwait(false) is not { BookReadAt: not null } snapshot)
        {
            return "no_snapshot";
        }

        return snapshot.Positions.Any(p => p.AccountId == accountId && p.NetQty != 0) || snapshot.Entries.Any(e => e.AccountId == accountId) || snapshot.OrderBook.Any(o => o.Status is "TRANSIT" or "PENDING" or "PART_TRADED")
            ? "not_flat"
            : null;
    }

    // The drill's own sender sees an open account so that the halt, and nothing else, is what stops the entry.
    private sealed class DrillMode : IModeSource
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(new ModeAnswer(ExecMode.SemiAuto, true));
    }

    private sealed class NoJournal : ISendJournal
    {
        public Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
