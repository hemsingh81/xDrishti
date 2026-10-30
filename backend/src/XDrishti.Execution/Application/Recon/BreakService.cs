using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Application.Recon;

public sealed record ReconRunOutcome(long RunId, bool Readable, int Breaks, int NewBreaks, bool Clean);

/// <summary>
/// Runs one reconciliation of one account and turns what it finds into records and halt requests, and nothing else (spec P12-06). It reads the broker and our books (read-only ports), compares with <see cref="Reconciler"/>, stores the run and the new breaks, asks for a
/// freeze (a foreign conflict asks for entries and a critical alarm) and, only when an end-of-day or final run is readable and clean, moves the account's closed intents to reconciled. A broker that cannot be read, or our record that cannot be read, is an <c>unavailable</c> break and a run that is
/// not clean. There is no code path that edits an intent, a leg, a fill or a position to make a number agree. The halt request is made on every run with a break, so a lost request is made again; the halt record does not stack repeats.
/// </summary>
public sealed class BreakService(IBrokerReads reads, IReconOurSource ours, IReconStore store, IHaltRequests halts, IAlarmStore alarms, IExecEventStore events, ReadBudget budget, TimeProvider clock, Func<ReconSettings?> settings)
{
    public const string Actor = "system:reconciliation";

    private readonly Dictionary<Guid, IReadOnlySet<string>> _missing = [];

    public async Task<ReconRunOutcome> RunAsync(Guid accountId, ReconKind kind, DateOnly sessionDate, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var s = settings();
        var unavailable = s is null ? "settings_unavailable" : budget.TryAdmit(3) ? null : "read_budget";
        BrokerRead<IReadOnlyList<BrokerOrderView>>? orders = null;
        BrokerRead<IReadOnlyList<BrokerTradeView>>? trades = null;
        BrokerRead<IReadOnlyList<BrokerPositionView>>? positions = null;
        if (unavailable is null)
        {
            orders = await reads.GetOrderBookAsync(cancellationToken).ConfigureAwait(false);
            trades = await reads.GetTradeBookAsync(cancellationToken).ConfigureAwait(false);
            positions = await reads.GetPositionsAsync(cancellationToken).ConfigureAwait(false);
            unavailable = !orders.Readable ? "order_book_unreadable" : !trades.Readable ? "trade_book_unreadable" : !positions.Readable ? "positions_unreadable" : null;
        }

        OurAccountView? mine = null;
        if (unavailable is null)
        {
            try
            {
                mine = await ours.LoadAsync(accountId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                unavailable = "our_record_unreadable";
            }
        }

        IReadOnlyList<ReconBreakDraft> found;
        int orderCount = 0, tradeCount = 0, positionCount = 0;
        if (unavailable is not null)
        {
            found = [new ReconBreakDraft(BreakKind.Unavailable, $"unavailable:{unavailable}", null, null, "n/a", "n/a", "the run could not read what it compares: " + unavailable)];
        }
        else
        {
            var accepted = await store.ResolvedKeysAsync(accountId, sessionDate, cancellationToken).ConfigureAwait(false);
            var result = Reconciler.Compare(new ReconInput(accountId, now, s!.Grace, mine!.Legs, mine.Fills, mine.Positions, orders!.Value!, trades!.Value!, positions!.Value!, accepted, _missing.GetValueOrDefault(accountId) ?? new HashSet<string>()));
            _missing[accountId] = result.MissingTrades;
            found = result.Breaks;
            (orderCount, tradeCount, positionCount) = (result.Orders, result.Trades, result.Positions);
        }

        var open = await store.OpenKeysAsync(accountId, cancellationToken).ConfigureAwait(false);
        var fresh = found.Where(b => !open.Contains(b.Key)).ToList();
        var runId = await store.RecordRunAsync(accountId, kind, unavailable is null, orderCount, tradeCount, positionCount, found.Count, fresh, now, cancellationToken).ConfigureAwait(false);

        foreach (var b in found)
        {
            await AskAsync(accountId, b, cancellationToken).ConfigureAwait(false);
        }

        var clean = unavailable is null && found.Count == 0;
        if (clean && kind is ReconKind.Eod or ReconKind.Final && mine is not null)
        {
            foreach (var intent in mine.Intents.Where(i => i.AccountId == accountId && i.State == ExecState.Closed))
            {
                await MoveReconciledAsync(intent.IntentId, now, cancellationToken).ConfigureAwait(false);
            }
        }

        return new ReconRunOutcome(runId, unavailable is null, found.Count, fresh.Count, clean);
    }

    private async Task AskAsync(Guid accountId, ReconBreakDraft b, CancellationToken cancellationToken)
    {
        var detail = $"{BreakKinds.Code(b.Kind)}: {b.Detail}";
        if (b.Kind == BreakKind.ForeignConflict)
        {
            await halts.RequestAsync(accountId, HaltLevel.Entries, Application.Halts.HaltTriggers.ForeignConflict, detail, cancellationToken).ConfigureAwait(false);
            await alarms.RaiseAsync(new AlarmDraft(AlarmCodes.ForeignConflict, accountId, null, detail.Length > 300 ? detail[..300] : detail, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            return;
        }

        await halts.RequestAsync(accountId, HaltLevel.Freeze, Application.Halts.HaltTriggers.ReconciliationBreak, detail, cancellationToken).ConfigureAwait(false);
    }

    private async Task MoveReconciledAsync(long intentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            if (await events.StateAsync(intentId, cancellationToken).ConfigureAwait(false) is { State: ExecState.Closed } state)
            {
                await events.AppendAsync(intentId, ExecEvent.Move(state.Seq + 1, ExecState.Closed, ExecState.Reconciled, ExecEventSource.Engine, Actor, now), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The run is already stored; the move is tried again by the next clean end-of-day run.
        }
    }
}

/// <summary>
/// Appends the adoption events of resolved breaks (spec P12-06 rule "Resolution"): for each <c>accept_broker</c> resolution of a break that belongs to an intent, one event of kind <c>leg_update</c>, source owner, the owner's actor, with the break id and the broker's figure. Nothing is rewritten: the
/// break stays in the history, the legs and fills keep their rows, and the later runs do not raise the accepted key again. Idempotent: an intent whose log already names the break is left alone.
/// </summary>
public sealed class BreakAdoption(IReconStore store, IExecEventStore events, TimeProvider clock)
{
    public async Task<int> AdoptAsync(CancellationToken cancellationToken)
    {
        var adopted = 0;
        foreach (var b in (await store.BreaksAsync(null, cancellationToken).ConfigureAwait(false)).Where(b => b.Resolution is { Resolution: "accept_broker" } && b.IntentId is not null))
        {
            var intentId = b.IntentId!.Value;
            var marker = $"\"adopt\":{b.Id}";
            if ((await events.EventsAsync(intentId, cancellationToken).ConfigureAwait(false)).Any(e => e.Kind == ExecEventKind.LegUpdate && e.Source == ExecEventSource.Owner && e.Payload.Contains(marker, StringComparison.Ordinal))
                || await events.StateAsync(intentId, cancellationToken).ConfigureAwait(false) is not { } state)
            {
                continue;
            }

            var payload = $"{{{marker},\"kind\":\"{BreakKinds.Code(b.Kind)}\"}}";
            var result = await events.AppendAsync(intentId, new ExecEvent(state.Seq + 1, ExecEventKind.LegUpdate, null, null, ExecEventSource.Owner, b.Resolution!.By, clock.GetUtcNow(), 0, payload), cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                adopted++;
            }
        }

        return adopted;
    }
}
