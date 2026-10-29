using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Application;

/// <summary>
/// The real pre-send step (P12-03, ADR 0159) for <see cref="LegSender"/>. An entry leg is judged by <see cref="ExecChecklist"/> on facts read now: with a still-valid cleared full run of the intent the seven time-critical checks re-run as a fast run, otherwise
/// a full run is made (and stored) and must itself clear. Every failure to load, judge or store is a refusal. Legs that reduce risk go to <see cref="IExitSendPolicy"/> and never touch the entry checklist. It can only refuse more than the default did.
/// </summary>
public sealed class ChecklistPreSendCheck(ICheckFactsLoader loader, ICheckStore store, IExitSendPolicy exits, TimeProvider clock) : IPreSendCheck
{
    public async Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leg);
        if (leg.Kind != LegKind.Entry)
        {
            return await exits.CheckAsync(accountId, intentId, leg, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (await loader.LoadAsync(accountId, intentId, leg, cancellationToken).ConfigureAwait(false) is not { } facts)
            {
                return new GateDecision(false, "check_facts_unavailable");
            }

            if (facts.Limits is not { } limits)
            {
                return new GateDecision(false, "check_limits_unavailable");
            }

            var now = clock.GetUtcNow();
            var stored = await store.LatestFullRunAsync(intentId, cancellationToken).ConfigureAwait(false);
            var fast = stored is { Verdict: not CheckVerdict.Rejected } && stored.ValidUntil >= now;
            var confirmation = fast ? null : await store.LatestConfirmationAsync(intentId, cancellationToken).ConfigureAwait(false);
            var run = ExecChecklist.Evaluate(fast ? CheckRunKind.Fast : CheckRunKind.Full, facts.Intent, facts.Account, facts.Market, facts.System, limits, now, confirmation);
            await store.AppendRunAsync(intentId, run, cancellationToken).ConfigureAwait(false);

            var cleared = fast ? run.Verdict == CheckVerdict.Cleared : run.AuthorisesSend;
            return cleared ? GateDecision.Allowed : new GateDecision(false, "check_failed:" + (run.NonPass.FirstOrDefault()?.Code ?? "unknown"));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new GateDecision(false, "check_store_unavailable");
        }
    }
}
