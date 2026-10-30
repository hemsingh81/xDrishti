using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Application.Recon;

/// <summary>A leg of a non-terminal intent as our record has it at start.</summary>
public sealed record RecoverLeg(Guid AccountId, long IntentId, string ClientOrderId, LegKind Kind, LegStatus Status, int Qty, int FilledQty, string? BrokerOrderId);

/// <summary>Writes what recovery learned, with source <c>recovery</c>. Adopt moves a leg to the broker's status by the rank rule; fail ends a leg the broker never had.</summary>
public interface IRecoveryRecord
{
    Task<IReadOnlyList<RecoverLeg>> OpenLegsAsync(CancellationToken cancellationToken);

    Task AdoptAsync(string clientOrderId, LegStatus status, int filledQty, string? brokerOrderId, CancellationToken cancellationToken);

    Task FailAsync(string clientOrderId, string reason, CancellationToken cancellationToken);
}

public enum AccountRecovery
{
    Recovering,
    Ready,
    Frozen,
}

public sealed record RecoveryReport(bool BrokerRead, DateTimeOffset? BrokerReadAt, IReadOnlyDictionary<Guid, AccountRecovery> Accounts, int Adopted, int Failed, int UnknownLegs, string? Reason)
{
    /// <summary>The host leaves <c>recovering</c> only when this is true: the broker was read and every account is ready or frozen.</summary>
    public bool Complete => BrokerRead && Accounts.Values.All(a => a != AccountRecovery.Recovering);
}

/// <summary>
/// Crash recovery (spec P12-06): the host starts <c>recovering</c> and writes nothing to the broker. This class cannot: it holds a lookup (a read by our client id), the read-only broker reads and the reconciliation, and no sender. Steps: (1) read the active halts, (2) load the non-terminal legs,
/// (3) look every leg in new or sent up by client id, twice 2 s apart (a lookup that itself fails leaves the leg unknown and the account not ready; nothing is ever sent again), (4) read order book, trade book, positions and funds, then adopt the legs the lookups found, fail the ones they proved were never sent and apply the broker's newer figures with source
/// <c>recovery</c>, (5) run a reconciliation of kind <c>recovery</c> per account, (6) an account with no break and no unknown leg is ready, one with a break is frozen (the break service asked for it), the rest stay recovering. An unreadable broker keeps everything recovering; after
/// <c>exec.recovery_max_s</c> twice over (120 s with the default) the alarm <c>recovery_slow</c> is raised. Protective repair after step 4 is allowed on every account including a frozen one, guarded by <see cref="RecoveryReport.BrokerReadAt"/>; the wiring is the host's, later.
/// </summary>
public sealed class RecoveryService(IRecoveryRecord record, LegLookup lookup, IBrokerReads reads, BreakService breaks, HaltService halts, IReconOurSource ours, IAlarmStore alarms, ReadBudget budget, TimeProvider clock, Func<ReconSettings?> settings)
{
    public static readonly TimeSpan SlowAlarmAfter = TimeSpan.FromSeconds(120);

    private DateTimeOffset? _firstAttempt;

    public async Task<RecoveryReport> RunAsync(DateOnly sessionDate, CancellationToken cancellationToken)
    {
        _firstAttempt ??= clock.GetUtcNow();
        if (await halts.ActiveAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            return await SlowAsync(new RecoveryReport(false, null, new Dictionary<Guid, AccountRecovery>(), 0, 0, 0, "halts_unreadable"), cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<RecoverLeg> open;
        IReadOnlyList<Guid> known;
        try
        {
            open = await record.OpenLegsAsync(cancellationToken).ConfigureAwait(false);
            known = await ours.AccountsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return await SlowAsync(new RecoveryReport(false, null, new Dictionary<Guid, AccountRecovery>(), 0, 0, 0, "record_unreadable"), cancellationToken).ConfigureAwait(false);
        }

        // Step 3 looks, step 4 reads; what the lookups found is written only once the broker could be read, so an unreadable broker changes nothing.
        var verdicts = new List<(RecoverLeg Leg, LookupVerdict Verdict)>();
        HashSet<Guid> unknown = [];
        foreach (var leg in open.Where(l => l.Status is LegStatus.New or LegStatus.Sent))
        {
            var verdict = await lookup.ResolveAsync(leg.ClientOrderId, cancellationToken).ConfigureAwait(false);
            verdicts.Add((leg, verdict.Verdict));
            if (verdict.Verdict == LookupVerdict.StillUnknown)
            {
                unknown.Add(leg.AccountId);
            }
        }

        if (!budget.TryAdmit(4))
        {
            return await SlowAsync(new RecoveryReport(false, null, new Dictionary<Guid, AccountRecovery>(), 0, 0, unknown.Count, "read_budget"), cancellationToken).ConfigureAwait(false);
        }

        var orders = await reads.GetOrderBookAsync(cancellationToken).ConfigureAwait(false);
        var trades = await reads.GetTradeBookAsync(cancellationToken).ConfigureAwait(false);
        var positions = await reads.GetPositionsAsync(cancellationToken).ConfigureAwait(false);
        var funds = await reads.GetFundsAsync(cancellationToken).ConfigureAwait(false);
        if (!orders.Readable || !trades.Readable || !positions.Readable || !funds.Readable)
        {
            return await SlowAsync(new RecoveryReport(false, null, new Dictionary<Guid, AccountRecovery>(), 0, 0, unknown.Count, "broker_unreadable"), cancellationToken).ConfigureAwait(false);
        }

        int adopted = 0, failed = 0;
        foreach (var (leg, verdict) in verdicts)
        {
            switch (verdict)
            {
                case LookupVerdict.Found:
                    await record.AdoptAsync(leg.ClientOrderId, LegStatus.Pending, leg.FilledQty, leg.BrokerOrderId, cancellationToken).ConfigureAwait(false);
                    adopted++;
                    break;
                case LookupVerdict.NotSent:
                    await record.FailAsync(leg.ClientOrderId, "not_sent_after_two_lookups", cancellationToken).ConfigureAwait(false);
                    failed++;
                    break;
                default:
                    break;
            }
        }

        var readAt = clock.GetUtcNow();
        var byClient = orders.Value!.Where(o => o.ClientOrderId is not null).GroupBy(o => o.ClientOrderId!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var leg in open.Where(l => verdicts.All(v => v.Leg.ClientOrderId != l.ClientOrderId || v.Verdict != LookupVerdict.NotSent)))
        {
            if (byClient.TryGetValue(leg.ClientOrderId, out var order) && UpdateApplier.ToLegStatus(order.Status, order.FilledQty, order.Qty) is { } status
                && (Leg.Rank(status) > Leg.Rank(leg.Status) || order.FilledQty > leg.FilledQty) && !Leg.IsFinal(leg.Status))
            {
                await record.AdoptAsync(leg.ClientOrderId, status, order.FilledQty, order.OrderId, cancellationToken).ConfigureAwait(false);
            }
        }

        // A leg whose lookup failed but that the order book now shows has been adopted above; one the book does not show stays unknown.
        unknown = [.. verdicts.Where(v => v.Verdict == LookupVerdict.StillUnknown && !byClient.ContainsKey(v.Leg.ClientOrderId)).Select(v => v.Leg.AccountId)];
        var accounts = known.Union(open.Select(l => l.AccountId)).Distinct().ToList();
        Dictionary<Guid, AccountRecovery> states = [];
        foreach (var account in accounts)
        {
            var run = await breaks.RunAsync(account, ReconKind.Recovery, sessionDate, cancellationToken).ConfigureAwait(false);
            states[account] = run.Breaks > 0 ? AccountRecovery.Frozen : unknown.Contains(account) ? AccountRecovery.Recovering : AccountRecovery.Ready;
        }

        var report = new RecoveryReport(true, readAt, states, adopted, failed, verdicts.Count(v => v.Verdict == LookupVerdict.StillUnknown && !byClient.ContainsKey(v.Leg.ClientOrderId)), null);
        return report.Complete ? report : await SlowAsync(report, cancellationToken).ConfigureAwait(false);
    }

    // Past the limit the report says so; past twice the limit an alarm is raised (once while it stays open).
    private async Task<RecoveryReport> SlowAsync(RecoveryReport report, CancellationToken cancellationToken)
    {
        var limit = settings()?.RecoveryMax ?? TimeSpan.FromSeconds(60);
        if (clock.GetUtcNow() - _firstAttempt >= TimeSpan.FromTicks(Math.Max(limit.Ticks * 2, SlowAlarmAfter.Ticks)))
        {
            await alarms.RaiseAsync(new AlarmDraft(AlarmCodes.RecoverySlow, null, null, $"recovery is not complete: {report.Reason ?? "accounts_not_ready"}", clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        }

        return report;
    }
}

/// <summary>Keeps legs in memory for tests and the CI rollout step; <see cref="Adopted"/> and <see cref="Failed"/> show what recovery wrote.</summary>
public sealed class InMemoryRecoveryRecord(IEnumerable<RecoverLeg> legs) : IRecoveryRecord
{
    private readonly Lock _gate = new();
    private readonly List<RecoverLeg> _legs = [.. legs];

    public List<string> Adopted { get; } = [];

    public List<string> Failed { get; } = [];

    public IReadOnlyList<RecoverLeg> Legs
    {
        get
        {
            lock (_gate)
            {
                return [.. _legs];
            }
        }
    }

    public Task<IReadOnlyList<RecoverLeg>> OpenLegsAsync(CancellationToken cancellationToken) => Task.FromResult(Legs);

    public Task AdoptAsync(string clientOrderId, LegStatus status, int filledQty, string? brokerOrderId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var at = _legs.FindIndex(l => l.ClientOrderId == clientOrderId);
            if (at >= 0)
            {
                _legs[at] = _legs[at] with { Status = status, FilledQty = Math.Max(_legs[at].FilledQty, filledQty), BrokerOrderId = brokerOrderId ?? _legs[at].BrokerOrderId };
                Adopted.Add(clientOrderId);
            }
        }

        return Task.CompletedTask;
    }

    public Task FailAsync(string clientOrderId, string reason, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var at = _legs.FindIndex(l => l.ClientOrderId == clientOrderId);
            if (at >= 0)
            {
                _legs[at] = _legs[at] with { Status = LegStatus.Rejected };
                Failed.Add(clientOrderId);
            }
        }

        return Task.CompletedTask;
    }
}
