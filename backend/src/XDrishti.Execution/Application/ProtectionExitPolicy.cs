using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Application;

/// <summary>
/// The reduce-only rule for legs that lower risk (P12-04 I3 to I5, Authority), as the <see cref="IExitSendPolicy"/> behind <see cref="LegSender"/>. It judges on a fresh snapshot; no snapshot, a stale book, no settings or any doubt is a refusal.
/// A stop covers only what no live stop covers and never sits below the intent's; an exit or a flatten sells at most the position less the exits already resting; nothing is ever sent for a net of zero or below; and under a freeze halt a fresh broker
/// position read must agree. This policy is NOT the default: the host composes <see cref="ClosedExitSendPolicy"/> and nothing composes this one until P12-06 supplies the real snapshot.
/// </summary>
public sealed class ProtectionExitPolicy(IProtectionSource source, Func<ProtectionSettings?> settings, TimeProvider clock) : IExitSendPolicy
{
    public static readonly TimeSpan FreezeReadMaxAge = TimeSpan.FromSeconds(15);

    public async Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leg);
        if (settings() is not { } s)
        {
            return Refuse("protection_settings_unavailable");
        }

        if (await source.ReadAsync(cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            return Refuse("protection_unknown");
        }

        if (snapshot.BookReadAt is not { } readAt || clock.GetUtcNow() - readAt > s.BlindAfter)
        {
            return Refuse("book_stale");
        }

        if (leg.Kind is not (LegKind.Stop or LegKind.Exit or LegKind.Flatten))
        {
            return Refuse(leg.Kind == LegKind.Target ? "target_not_resting" : "not_a_reducing_leg");
        }

        if (leg.Side != ExecSide.Sell)
        {
            return Refuse("reduce_only_side");
        }

        var position = snapshot.Positions.FirstOrDefault(p => p.IntentId == intentId && p.AccountId == accountId && p.SecurityId == leg.SecurityId);
        if (position is null || position.NetQty == 0)
        {
            return Refuse("no_position");
        }

        if (position.NetQty < 0)
        {
            return Refuse("oversold_nothing_sent");
        }

        if (leg.Qty <= 0)
        {
            return Refuse("quantity");
        }

        if (leg.Kind == LegKind.Stop)
        {
            var covered = ProtectionBook.StopLegs(snapshot.OrderBook, leg.SecurityId).Where(l => l.Status == ProtectionMonitor.StatusPending).Sum(l => l.RemainingQty);
            if (leg.Trigger is not { } trigger || leg.Price > trigger)
            {
                return Refuse("stop_limit_above_trigger");
            }

            if (trigger < position.IntentStop)
            {
                return Refuse("stop_looser_than_intent");
            }

            if (leg.Qty > position.NetQty - covered)
            {
                return Refuse("stop_exceeds_uncovered");
            }
        }
        else if (leg.Qty > StopRules.MaxExitQty(position.NetQty, ProtectionBook.ExitsInFlight(snapshot.OrderBook, leg.SecurityId)))
        {
            return Refuse("exceeds_position");
        }

        if (snapshot.Halt == HaltLevel.Freeze)
        {
            if (!FreezeAgrees(position, clock.GetUtcNow()))
            {
                return Refuse("freeze_needs_position_read");
            }

            if (leg.Qty > position.BrokerNetQty)
            {
                return Refuse("freeze_broker_net_below_ours");
            }
        }

        return GateDecision.Allowed;
    }

    /// <summary>A broker position read at most 15 s old whose net is at least ours.</summary>
    public static bool FreezeAgrees(ProtectedPosition p, DateTimeOffset now) =>
        p.BrokerPositionAt is { } at && p.BrokerNetQty is { } broker && now - at <= FreezeReadMaxAge && broker >= p.NetQty;

    private static GateDecision Refuse(string reason) => new(false, reason);
}
