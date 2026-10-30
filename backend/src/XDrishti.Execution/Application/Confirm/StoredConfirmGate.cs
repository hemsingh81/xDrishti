using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Confirm;

/// <summary>
/// The confirm gate over the stored outcomes (spec P12-08 criterion 12, D6): open only when the newest ticket of the intent is <c>confirmed</c> and the decision is at most 30 seconds old by the database clock. An older confirmation answers
/// <see cref="ConfirmPolicy.ExpiredAfterConfirm"/> (the intent is then cancelled, never sent late, so a restart between the tap and the send sends nothing stale); no ticket, an open one, a skip, an expiry or a refusal answers closed; a read that
/// fails answers closed. Composed by no host.
/// </summary>
public sealed class StoredConfirmGate(IConfirmDecisions decisions) : IConfirmGate
{
    public async Task<GateDecision> CheckAsync(long intentId, CancellationToken cancellationToken)
    {
        ConfirmDecision? decision;
        try
        {
            decision = await decisions.NewestAsync(intentId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new GateDecision(false, "confirm_unreadable");
        }

        return decision switch
        {
            null => new GateDecision(false, "no_ticket"),
            { Outcome: null } => new GateDecision(false, "awaiting_tap"),
            { Outcome: not TicketOutcomes.Confirmed } => new GateDecision(false, decision.Outcome),
            { DecidedAt: { } at } when decision.DbNow - at <= ConfirmPolicy.ConfirmedFreshFor && at <= decision.DbNow + TimeSpan.FromSeconds(1) => new GateDecision(true, TicketOutcomes.Confirmed),
            _ => new GateDecision(false, ConfirmPolicy.ExpiredAfterConfirm),
        };
    }
}
