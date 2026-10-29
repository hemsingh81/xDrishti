using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Application;

/// <summary>
/// The end of the day (P12-04): every working or part-filled entry is cancelled at <c>exec.entry_window_end</c> and again at the square-off time, and the cancel is verified in the book before any exit is sent; an entry still working after the
/// square-off is CRITICAL <c>exit_failed</c> and no exit is sent on top of it. Then the target leg is cancelled and the exit is a reduce-only marketable limit, re-priced while it rests; the stop is cancelled only after the position is flat, and an
/// exit that is larger than what the stop left is cancelled at once. A target leg that cannot be cancelled here (a super-order leg: its path is not built) stops the exit with <c>exit_failed</c> rather than risk a double sell. If the stop and the
/// exit both fill, the net is negative: the watchdog raises <c>oversold</c> and nothing is sent to correct it.
/// </summary>
public sealed class SquareOffService(IProtectionSource source, LegSender sender, IBrokerOrders broker, ProtectiveGuard guard, ProtectionReporter report, Func<ProtectionSettings?> settings, TimeProvider clock)
{
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30);
    private readonly HashSet<long> _entryCancelTried = [];
    private readonly Dictionary<long, (int Sent, DateTimeOffset At, bool Unknown)> _exits = [];

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (settings() is not { } s || await source.ReadAsync(cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var ist = TimeOnly.FromDateTime(now.ToOffset(IstOffset).DateTime);
        foreach (var group in snapshot.Entries.GroupBy(e => e.IntentId))
        {
            await EntriesAsync(group.ToList(), ist, s, snapshot, cancellationToken).ConfigureAwait(false);
        }

        foreach (var p in snapshot.Positions.Where(p => p.NetQty > 0 || ProtectionBook.StopLegs(snapshot.OrderBook, p.SecurityId).Any(l => l.Status == ProtectionMonitor.StatusPending)))
        {
            await PositionAsync(p, snapshot, s, ist, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EntriesAsync(List<WorkingEntry> entries, TimeOnly ist, ProtectionSettings s, ProtectionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var first = entries[0];
        var step = SquareOffPlan.Next(new SquareOffState(ist, s.EntryWindowEnd, s.SquareOffTime, entries.Count, _entryCancelTried.Contains(first.IntentId), false, false, 0, 0, false));
        if (step.Kind == SquareOffStepKind.ExitFailed)
        {
            await report.RaiseAsync(AlarmCodes.ExitFailed, first.AccountId, first.IntentId, "an entry is still working after the square-off time", cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
        }

        if (step.Kind is SquareOffStepKind.CancelEntries or SquareOffStepKind.ExitFailed && (await guard.OpenAsync(first.AccountId, cancellationToken).ConfigureAwait(false)).Open)
        {
            foreach (var entry in entries)
            {
                await broker.CancelAsync(entry.ClientOrderId, cancellationToken).ConfigureAwait(false);
            }

            _entryCancelTried.Add(first.IntentId);
        }
    }

    private async Task PositionAsync(ProtectedPosition p, ProtectionSnapshot snapshot, ProtectionSettings s, TimeOnly ist, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entries = snapshot.Entries.Count(e => e.IntentId == p.IntentId);
        var stopLegs = ProtectionBook.StopLegs(snapshot.OrderBook, p.SecurityId).Where(l => l.Status == ProtectionMonitor.StatusPending).ToList();
        var targets = snapshot.OrderBook.Where(o => o.SecurityId == p.SecurityId && o.Side == ExecSide.Sell && string.Equals(o.LegName, "TARGET_LEG", StringComparison.OrdinalIgnoreCase) && o.Status is "PENDING" or "TRANSIT").ToList();
        var resting = ProtectionBook.ExitsResting(snapshot.OrderBook, p.SecurityId);
        var inFlight = resting.Sum(o => Math.Max(0, o.Qty - o.FilledQty));
        if (resting.Count > 0 && _exits.TryGetValue(p.IntentId, out var seen) && seen.Unknown)
        {
            _exits[p.IntentId] = seen with { Unknown = false }; // the exit is visible in the book: its outcome is known
        }

        var step = SquareOffPlan.Next(new SquareOffState(ist, s.EntryWindowEnd, s.SquareOffTime, entries, _entryCancelTried.Contains(p.IntentId), targets.Count > 0, false, p.NetQty, inFlight, stopLegs.Count > 0));
        if (!(await guard.OpenAsync(p.AccountId, cancellationToken).ConfigureAwait(false)).Open || entries > 0)
        {
            return; // entries first; the exit sequence starts only when none is working
        }

        // The stop filled first: an exit larger than what is left is cancelled at once, so it cannot sell shares the stop already sold.
        if (inFlight > p.NetQty)
        {
            foreach (var order in resting.Where(o => o.ClientOrderId is not null))
            {
                await broker.CancelAsync(order.ClientOrderId!, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        switch (step.Kind)
        {
            case SquareOffStepKind.CancelTarget:
                foreach (var target in targets)
                {
                    if (target.ClientOrderId is null || !ClientOrderId.TryParse(target.ClientOrderId, out _))
                    {
                        await report.RaiseAsync(AlarmCodes.ExitFailed, p.AccountId, p.IntentId, "the target leg cannot be cancelled here; no exit is sent on top of it", cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
                        return;
                    }

                    await broker.CancelAsync(target.ClientOrderId, cancellationToken).ConfigureAwait(false);
                }

                break;
            case SquareOffStepKind.SendExit:
                await ExitAsync(p, snapshot, s, step.Qty, now, cancellationToken).ConfigureAwait(false);
                break;
            case SquareOffStepKind.CancelStop:
                foreach (var order in snapshot.OrderBook.Where(o => o.SecurityId == p.SecurityId && o.Status == ProtectionMonitor.StatusPending && ProtectionBook.IsStop(o) && o.ClientOrderId is not null))
                {
                    await broker.CancelAsync(order.ClientOrderId!, cancellationToken).ConfigureAwait(false);
                }

                break;
            case SquareOffStepKind.None when inFlight > 0 && p.NetQty > 0 && _exits.TryGetValue(p.IntentId, out var sent) && now - sent.At >= ProtectionWatchdog.StepGap && sent.Sent < s.FlattenWalkSteps + ProtectionWatchdog.RetryAttempts:
                // The exit rests unfilled: cancel it; the next tick sends it again at the next (wider) price.
                foreach (var order in resting.Where(o => o.ClientOrderId is not null))
                {
                    await broker.CancelAsync(order.ClientOrderId!, cancellationToken).ConfigureAwait(false);
                }

                break;
            default:
                break;
        }
    }

    private async Task ExitAsync(ProtectedPosition p, ProtectionSnapshot snapshot, ProtectionSettings s, int qty, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (p.Last is not { } last || last <= 0m)
        {
            await report.RaiseAsync(AlarmCodes.ProtectionUnknown, p.AccountId, p.IntentId, "no last price for the square-off exit", cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
            return;
        }

        var sent = _exits.TryGetValue(p.IntentId, out var known) ? known.Sent : 0;

        // shortcut: a second exit is held back, not looked up. A send with an unknown outcome, one whose order the book read has not yet had a chance to show (the book was read before the send),
        // or one under StepGap old is not repeated unless ResendPolicy allows (exec.lookup_verified, default OFF). Upgrade: resolve with LegLookup instead of waiting for the book, when the owner has verified the lookup path (canary C1).
        if (sent > 0 && !ResendPolicy.MayResend(LegKind.Exit, sent, s.LookupVerified)
            && (known.Unknown || now - known.At < ProtectionWatchdog.StepGap || snapshot.BookReadAt is not { } readAt || readAt <= known.At))
        {
            if (known.Unknown)
            {
                await report.RaiseAsync(AlarmCodes.SendUnknown, p.AccountId, p.IntentId, "the square-off exit has an unknown outcome", cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var made = ClientOrderId.Create(snapshot.SessionDate, p.IntentId, LegKind.Exit, sent + 1);
        if (made.IsFailure)
        {
            await report.RaiseAsync(AlarmCodes.ExitFailed, p.AccountId, p.IntentId, "no more exit attempts", cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
            return;
        }

        var price = StopRules.FlattenPrice(last, s.ExitCollarPct, s.FlattenStepCollarPct, Math.Min(sent, s.FlattenWalkSteps - 1));
        var result = await sender.SendAsync(new SendRequest(p.AccountId, p.IntentId, new BrokerLeg(made.Value.ToString(), LegKind.Exit, ExecSide.Sell, p.SecurityId, qty, price), now, last), cancellationToken).ConfigureAwait(false);
        if (result.Status == SendStatus.Sent)
        {
            var unknown = result.Ack?.Outcome == BrokerOutcome.Unknown;
            _exits[p.IntentId] = (sent + 1, now, unknown);
            if (unknown)
            {
                await report.RaiseAsync(AlarmCodes.SendUnknown, p.AccountId, p.IntentId, "the square-off exit has an unknown outcome", cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await report.RaiseAsync(AlarmCodes.ExitFailed, p.AccountId, p.IntentId, "square-off exit refused: " + result.Reason, cancellationToken, HaltLevel.Entries, snapshot.Halt).ConfigureAwait(false);
        }
    }
}
