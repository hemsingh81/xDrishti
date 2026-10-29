using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Application;

/// <summary>
/// The protection invariant, enforced once per tick (P12-04, ADR 0160). Each tick reads one snapshot and judges every open position with <see cref="ProtectionMonitor"/>: pending inside the deadline; at the deadline a CRITICAL
/// <c>protection_breach</c>, a halt request for entries, one repair (a stop for the uncovered quantity, attempt 2) and, if it fails or does not appear within another deadline and <c>exec.flatten_on_unprotected</c> is on, a flatten walk of reduce-only
/// marketable limits inside the exit collar. A blind or unreadable view is <c>protection_unknown</c> and never a flatten and never a pass. A negative net is <c>oversold</c>: a freeze request and nothing sent. Every send goes through
/// <see cref="LegSender"/> and its exit policy, so it can only reduce risk. A send whose outcome is unknown is never repeated unless <c>exec.lookup_verified</c> is on and a lookup finds the leg absent.
/// </summary>
public sealed class ProtectionWatchdog(IProtectionSource source, LegSender sender, IBrokerOrders broker, ProtectiveGuard guard, ProtectionReporter report, Func<ProtectionSettings?> settings, TimeProvider clock)
{
    public const int RepairAttempt = 2;
    public const int RetryAttempts = 3;
    public static readonly TimeSpan StepGap = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan RetryGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CancelGap = TimeSpan.FromSeconds(2);

    private static readonly string[] AutoClear = [AlarmCodes.ProtectionPending, AlarmCodes.ProtectionBreach, AlarmCodes.ProtectionUnknown, AlarmCodes.StopMismatch, AlarmCodes.FlattenStarted, AlarmCodes.FlattenIncomplete, AlarmCodes.ExitFailed];

    private readonly Dictionary<long, Track> _tracks = [];

    private sealed class Track
    {
        public DateTimeOffset? Since { get; set; }

        public DateTimeOffset? RepairAt { get; set; }

        public bool RepairFailed { get; set; }

        public bool FlattenStarted { get; set; }

        public int FlattenSent { get; set; }

        public bool FlattenUnknown { get; set; }

        /// <summary>The repair stop was sent and its outcome is unknown: pending lookup, not failed. Nothing is sent again and no flatten starts while it is.</summary>
        public bool RepairPending { get; set; }

        public int StopCancelTries { get; set; }

        public DateTimeOffset? LastFlattenAt { get; set; }

        public DateTimeOffset? CancelSentAt { get; set; }
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var s = settings();
        var snapshot = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null || s is null)
        {
            await report.RaiseAsync(AlarmCodes.ProtectionUnknown, null, null, snapshot is null ? "no_snapshot" : "settings_unavailable", cancellationToken, HaltLevel.Entries, snapshot?.Halt).ConfigureAwait(false);
            return;
        }

        foreach (var position in snapshot.Positions)
        {
            await HandleAsync(position, snapshot, s, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(ProtectedPosition p, ProtectionSnapshot snapshot, ProtectionSettings s, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var track = _tracks.TryGetValue(p.IntentId, out var existing) ? existing : _tracks[p.IntentId] = new Track();
        var legs = ProtectionBook.StopLegs(snapshot.OrderBook, p.SecurityId);
        var covered = legs.Where(l => l.Status == ProtectionMonitor.StatusPending && l.Trigger >= p.IntentStop).Sum(l => l.RemainingQty);
        if (p.NetQty - covered <= 0)
        {
            track.Since = null;
        }
        else if (track.Since is null)
        {
            track.Since = p.RaisedAt is { } raised && raised < now ? raised : now;
        }

        var verdict = ProtectionMonitor.Judge(new ProtectionInput(p.NetQty, legs, p.IntentStop, track.Since, now, snapshot.BookReadAt, s.Deadline, s.BlindAfter));
        switch (verdict)
        {
            case ProtectionVerdict.Flat:
                await ClearAsync(p, cancellationToken).ConfigureAwait(false);
                if (p.State is ExecState.Filled or ExecState.Protected or ExecState.Managing or ExecState.Closing)
                {
                    await report.MoveAsync(p.IntentId, ExecState.Closed, cancellationToken).ConfigureAwait(false);
                }

                await CancelLeftoverStopsAsync(p, snapshot, track, now, cancellationToken).ConfigureAwait(false);
                break;
            case ProtectionVerdict.Protected:
                if (track.RepairAt is not null)
                {
                    await report.RaiseAsync(AlarmCodes.StopReplaced, p.AccountId, p.IntentId, "stop restored after a breach", cancellationToken).ConfigureAwait(false);
                }

                await ClearAsync(p, cancellationToken).ConfigureAwait(false);
                if (p.State == ExecState.Filled || (p.State == ExecState.Closing && !track.FlattenStarted))
                {
                    await report.MoveAsync(p.IntentId, ExecState.Protected, cancellationToken).ConfigureAwait(false);
                }

                _tracks[p.IntentId] = new Track();
                break;
            case ProtectionVerdict.Pending pending:
                await report.RaiseAsync(AlarmCodes.ProtectionPending, p.AccountId, p.IntentId, $"{pending.Uncovered} shares without a stop until {pending.DeadlineAt:O}", cancellationToken).ConfigureAwait(false);
                break;
            case ProtectionVerdict.Unknown unknown:
                await report.RaiseAsync(AlarmCodes.ProtectionUnknown, p.AccountId, p.IntentId, unknown.Reason, cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
                break;
            case ProtectionVerdict.Breach { Reason: "oversold" }:
                await report.RaiseAsync(AlarmCodes.Oversold, p.AccountId, p.IntentId, $"net {p.NetQty}", cancellationToken, HaltLevel.Freeze, snapshot.Halt).ConfigureAwait(false);
                break;
            case ProtectionVerdict.Breach { Reason: "stop_mismatch" }:
                await report.RaiseAsync(AlarmCodes.StopMismatch, p.AccountId, p.IntentId, "a stop is looser than the intent's", cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
                break;
            case ProtectionVerdict.Breach breach:
                await BreachAsync(p, snapshot, s, track, breach.Uncovered, now, cancellationToken).ConfigureAwait(false);
                break;
            default:
                break;
        }
    }

    private async Task BreachAsync(ProtectedPosition p, ProtectionSnapshot snapshot, ProtectionSettings s, Track track, int uncovered, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await report.ClearAsync(AlarmCodes.ProtectionPending, p.AccountId, p.IntentId, cancellationToken).ConfigureAwait(false);
        if (snapshot.Halt == HaltLevel.Freeze && !ProtectionExitPolicy.FreezeAgrees(p, now))
        {
            // Our picture is untrusted under a freeze: without a fresh broker position that agrees, nothing is sent.
            await report.RaiseAsync(AlarmCodes.ProtectionUnknown, p.AccountId, p.IntentId, "freeze: no fresh broker position read that agrees", cancellationToken).ConfigureAwait(false);
            return;
        }

        await report.RaiseAsync(AlarmCodes.ProtectionBreach, p.AccountId, p.IntentId, $"{uncovered} of {p.NetQty} shares without a confirmed broker-side stop", cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);

        if (track.RepairPending)
        {
            // shortcut: a repair whose outcome is unknown waits for the book (the stop appears, or the breach clears). It is resolved by lookup only once the owner has verified the lookup path (exec.lookup_verified).
            // Upgrade: call LegLookup here when that setting is on and send attempt 3 on a confirmed absence.
            await report.RaiseAsync(AlarmCodes.SendUnknown, p.AccountId, p.IntentId, "the repair stop has an unknown outcome; pending lookup", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (track.RepairAt is null)
        {
            await RepairAsync(p, snapshot, s, track, uncovered, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        if ((track.RepairFailed || now - track.RepairAt >= s.Deadline) && s.FlattenOnUnprotected)
        {
            await FlattenAsync(p, snapshot, s, track, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RepairAsync(ProtectedPosition p, ProtectionSnapshot snapshot, ProtectionSettings s, Track track, int uncovered, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!ResendAllowed(snapshot, p, LegKind.Stop, 1, s))
        {
            await report.RaiseAsync(AlarmCodes.SendUnknown, p.AccountId, p.IntentId, "a stop is in transit and its outcome is not resolved", cancellationToken).ConfigureAwait(false);
            return;
        }

        var cid = Id(snapshot, p, LegKind.Stop, RepairAttempt);
        var plan = ProtectionPlan.StopFor(uncovered, p.IntentStop, s.StopLimitBufferPct);
        track.RepairAt = now;
        if (cid is null || plan is null)
        {
            track.RepairFailed = true;
            return;
        }

        var result = await sender.SendAsync(new SendRequest(p.AccountId, p.IntentId, new BrokerLeg(cid, LegKind.Stop, ExecSide.Sell, p.SecurityId, plan.Qty, plan.Limit, plan.Trigger), now, p.Last), cancellationToken).ConfigureAwait(false);
        track.RepairPending = result.Ack?.Outcome == BrokerOutcome.Unknown;
        track.RepairFailed = !track.RepairPending && (result.Status != SendStatus.Sent || result.Ack?.Outcome != BrokerOutcome.Acknowledged);
        if (track.RepairPending)
        {
            await report.RaiseAsync(AlarmCodes.SendUnknown, p.AccountId, p.IntentId, "the repair stop has an unknown outcome", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FlattenAsync(ProtectedPosition p, ProtectionSnapshot snapshot, ProtectionSettings s, Track track, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!track.FlattenStarted)
        {
            track.FlattenStarted = true;
            await report.RaiseAsync(AlarmCodes.FlattenStarted, p.AccountId, p.IntentId, $"flatten of {p.NetQty} shares", cancellationToken).ConfigureAwait(false);
            await report.MoveAsync(p.IntentId, ExecState.Closing, cancellationToken).ConfigureAwait(false);
        }

        var inWalk = track.FlattenSent < s.FlattenWalkSteps;
        var gap = inWalk ? StepGap : RetryGap;
        if (track.LastFlattenAt is { } last && now - last < gap)
        {
            return;
        }

        if (track.FlattenSent >= s.FlattenWalkSteps)
        {
            await report.RaiseAsync(AlarmCodes.FlattenIncomplete, p.AccountId, p.IntentId, $"{track.FlattenSent} flatten attempts, {p.NetQty} shares still open", cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
            if (track.FlattenSent >= s.FlattenWalkSteps + RetryAttempts)
            {
                return; // the owner takes over
            }
        }

        // A resting flatten of ours is cancelled first; the next one is sent only when the book no longer shows it, so two flatten sells never rest together.
        var resting = ProtectionBook.ExitsResting(snapshot.OrderBook, p.SecurityId);
        if (resting.Count > 0)
        {
            if (!(await guard.OpenAsync(p.AccountId, cancellationToken).ConfigureAwait(false)).Open)
            {
                return;
            }

            if (track.CancelSentAt is not { } sent || now - sent >= CancelGap)
            {
                foreach (var order in resting.Where(o => o.ClientOrderId is not null))
                {
                    await broker.CancelAsync(order.ClientOrderId!, cancellationToken).ConfigureAwait(false);
                }

                track.CancelSentAt = now;
            }

            return;
        }

        if (resting.Count > 0)
        {
            track.FlattenUnknown = false; // visible in the book
        }

        // A resting stop of ours would sell the same shares as the flatten: cancel it and wait until the book no longer shows it. A cancel that cannot be confirmed means no flatten.
        if (RestingStops(snapshot, p.SecurityId) is { Count: > 0 } stops)
        {
            await CancelStopsAsync(p, stops, track, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (track.FlattenUnknown && !ResendPolicy.MayResend(LegKind.Flatten, track.FlattenSent, s.LookupVerified))
        {
            await report.RaiseAsync(AlarmCodes.SendUnknown, p.AccountId, p.IntentId, "the last flatten has an unknown outcome", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (p.Last is not { } price || price <= 0m)
        {
            await report.RaiseAsync(AlarmCodes.ProtectionUnknown, p.AccountId, p.IntentId, "no last price to flatten against", cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
            return;
        }

        var qty = StopRules.MaxExitQty(p.NetQty, ProtectionBook.ExitsInFlight(snapshot.OrderBook, p.SecurityId));
        var cid = Id(snapshot, p, LegKind.Flatten, track.FlattenSent + 1);
        track.LastFlattenAt = now;
        if (qty == 0 || cid is null)
        {
            return;
        }

        var limit = StopRules.FlattenPrice(price, s.ExitCollarPct, s.FlattenStepCollarPct, Math.Min(track.FlattenSent, s.FlattenWalkSteps - 1));
        var result = await sender.SendAsync(new SendRequest(p.AccountId, p.IntentId, new BrokerLeg(cid, LegKind.Flatten, ExecSide.Sell, p.SecurityId, qty, limit), now, price), cancellationToken).ConfigureAwait(false);
        if (result.Status != SendStatus.Sent)
        {
            await report.RaiseAsync(AlarmCodes.ExitFailed, p.AccountId, p.IntentId, "flatten refused: " + result.Reason, cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
            return;
        }

        track.FlattenSent++;
        track.FlattenUnknown = result.Ack?.Outcome == BrokerOutcome.Unknown;
        if (track.FlattenUnknown)
        {
            await report.RaiseAsync(AlarmCodes.SendUnknown, p.AccountId, p.IntentId, "the flatten has an unknown outcome", cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly string[] RestingStatus = ["TRANSIT", "PENDING", "PART_TRADED"];

    private static List<BrokerOrderView> RestingStops(ProtectionSnapshot snapshot, string securityId) =>
        [.. snapshot.OrderBook.Where(o => o.SecurityId == securityId && ProtectionBook.IsStop(o) && RestingStatus.Contains(o.Status))];

    // Cancels our resting stops (guarded). After three tries that the book still shows, or for a stop that has no client id of ours (a super-order leg: its cancel path is not built), exit_failed is raised and the caller sends nothing.
    private async Task CancelStopsAsync(ProtectedPosition p, List<BrokerOrderView> stops, Track track, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (stops.Any(o => o.ClientOrderId is null) || track.StopCancelTries >= 3)
        {
            await report.RaiseAsync(AlarmCodes.ExitFailed, p.AccountId, p.IntentId, "a resting stop cannot be cancelled or confirmed gone; no flatten is sent on top of it", cancellationToken, HaltLevel.Entries, null).ConfigureAwait(false);
            return;
        }

        if (!(await guard.OpenAsync(p.AccountId, cancellationToken).ConfigureAwait(false)).Open)
        {
            return;
        }

        if (track.CancelSentAt is { } sent && now - sent < CancelGap)
        {
            return;
        }

        foreach (var order in stops)
        {
            await broker.CancelAsync(order.ClientOrderId!, cancellationToken).ConfigureAwait(false);
        }

        track.CancelSentAt = now;
        track.StopCancelTries++;
    }

    // Once flat, a SELL stop left resting could open a short: cancel it. The track is kept until the book shows none.
    private async Task CancelLeftoverStopsAsync(ProtectedPosition p, ProtectionSnapshot snapshot, Track track, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (RestingStops(snapshot, p.SecurityId) is { Count: > 0 } stops)
        {
            await CancelStopsAsync(p, stops, track, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        _tracks.Remove(p.IntentId);
    }

    // shortcut: "in transit" is read from the order book only, one leg kind per security, and a lookup is never made here. Upgrade: resolve with LegLookup and key legs by (parent, LegNo, id) once the owner has verified the lookup path (canary C1).
    // A leg of this kind in TRANSIT (or a send that came back Unknown) is not known to be absent, so sending another is a resend: allowed only under ResendPolicy, which is closed until the owner sets exec.lookup_verified.
    private static bool ResendAllowed(ProtectionSnapshot snapshot, ProtectedPosition p, LegKind kind, int attempt, ProtectionSettings s)
    {
        var inTransit = snapshot.OrderBook.Any(o => o.SecurityId == p.SecurityId && o.Status == "TRANSIT" && o.ClientOrderId is { } c && ClientOrderId.TryParse(c, out var id) && id.Kind == kind);
        return !inTransit || ResendPolicy.MayResend(kind, attempt, s.LookupVerified);
    }

    private async Task ClearAsync(ProtectedPosition p, CancellationToken cancellationToken)
    {
        foreach (var code in AutoClear)
        {
            await report.ClearAsync(code, p.AccountId, p.IntentId, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? Id(ProtectionSnapshot snapshot, ProtectedPosition p, LegKind kind, int attempt)
    {
        var made = ClientOrderId.Create(snapshot.SessionDate, p.IntentId, kind, attempt);
        return made.IsSuccess ? made.Value.ToString() : null;
    }
}
