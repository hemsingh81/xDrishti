using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The daily evaluation of one strategy (spec CF-12 D2 and D3, part 12b, ADR 0219). It reads the decay report (the one source of the kill verdicts, so the proposal and the read agree), writes one proposal for every rule that <b>fires</b> and has cells to pause (a rule
/// that is not evaluated, held or holding never makes one; a rule with an open proposal does not make a second; at most one per strategy, rule and day), records the evaluation, and only then, when the stored setting <c>confidence.kill.auto</c> is ON, asks to apply the open
/// proposals that are not proposal-only (the CF-09 failure boundary is never offered to the automatic path). With the setting OFF, which is the default and the case when no row exists, it changes nothing but its own proposals and evaluation. A failure before the evaluation is
/// recorded leaves nothing recorded for the day, so the next run repeats it. No order is placed.
/// </summary>
internal sealed class ProposeKillHandler(
    IQueryHandler<GetDecayReportQuery, Result<DecayReportView>> report,
    IKillProposalReader reader,
    IKillProposalStore store,
    IDecayCheckStore checks,
    IKillSettings settings,
    StrategyCells cells,
    ICommandHandler<ApplyKillCommand, ApplyKillOutcome> apply,
    DecayOptions decay,
    KillOptions kill,
    TimeProvider clock,
    ILogger<ProposeKillHandler> logger) : ICommandHandler<ProposeKillCommand, ProposeKillOutcome>
{
    private const int OpenLimit = 100;
    private const int MaxText = 900;

    public async ValueTask<Result<ProposeKillOutcome>> HandleAsync(ProposeKillCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var strategy = command.Strategy?.Trim();
        if (string.IsNullOrEmpty(strategy) || strategy.Contains('|', StringComparison.Ordinal))
        {
            return Result.Failure<ProposeKillOutcome>(PaperErrors.Invalid("strategy", "The kill evaluation needs a strategy id without `|`."));
        }

        var today = IndiaTime.Today(clock.GetUtcNow());
        if (await checks.ExistsAsync(strategy, today, cancellationToken))
        {
            // The evaluation of today is recorded, but an automatic apply that failed after it was recorded is retried here: the proposals made today are still open and the setting is read again.
            var again = await ApplyOpenAutomaticallyAsync(strategy, today, null, cancellationToken);
            return Result.Success(new ProposeKillOutcome(strategy, true, [], again.Applied, 0, again.Failed));
        }

        var read = await report.HandleAsync(new GetDecayReportQuery(strategy), cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<ProposeKillOutcome>(read.Error);
        }

        var view = read.Value;
        var settingsHash = CanonicalText.Sha256Hex(decay.KeyText() + "|" + kill.KeyText());
        var demotable = await cells.DemotableAsync(strategy, cancellationToken);
        var open = await reader.ListAsync("proposed", strategy, OpenLimit, cancellationToken);
        var created = new List<long>();
        var withoutProposal = 0;
        foreach (var verdict in view.Kill.Where(v => KillDecision.MakesProposal(KillStates.Parse(v.State))))
        {
            if (demotable.Count == 0 || open.Any(p => p.Proposal.Rule == verdict.Rule))
            {
                withoutProposal++;
                continue;
            }

            var text = verdict.Text.Length > MaxText ? verdict.Text[..MaxText] : verdict.Text;
            var id = await store.InsertAsync(new NewKillProposal(strategy, view.Epoch, verdict.Rule, verdict.ProposalOnly, verdict.Value, verdict.Limit, verdict.Trades, text, demotable, settingsHash, view.Edge.Trades), today, cancellationToken);
            if (id is { } stored)
            {
                created.Add(stored);
                KillLog.Proposed(logger, stored, verdict.Rule, demotable.Count);
            }
        }

        await checks.InsertAsync(new NewDecayCheck(strategy, view.Epoch, settingsHash, view.Edge.State, view.Edge.Trades, JsonSerializer.Serialize(view.Kill)), today, cancellationToken);
        var firing = view.Kill.Where(v => KillDecision.MakesProposal(KillStates.Parse(v.State))).Select(v => v.Rule).ToHashSet(StringComparer.Ordinal);
        var applied = await ApplyOpenAutomaticallyAsync(strategy, today, firing, cancellationToken);
        return Result.Success(new ProposeKillOutcome(strategy, false, created, applied.Applied, withoutProposal, applied.Failed));
    }

    /// <summary>
    /// With the stored setting ON, applies the open proposals the automatic path may execute: not proposal-only ones, and only those that were made today or whose rule fires in today's report (<paramref name="firing"/>; null when the report was not read). An older open proposal
    /// whose rule no longer fires stays for the owner. With the setting OFF, or with no row, applies nothing.
    /// </summary>
    private async ValueTask<(int Applied, int Failed)> ApplyOpenAutomaticallyAsync(string strategy, DateOnly today, HashSet<string>? firing, CancellationToken cancellationToken)
    {
        var current = await settings.CurrentAsync(cancellationToken);
        if (!current.Auto)
        {
            return (0, 0);
        }

        int applied = 0, failed = 0;
        foreach (var proposal in await reader.ListAsync("proposed", strategy, OpenLimit, cancellationToken))
        {
            if (!KillDecision.MayExecuteAutomatically(current.Auto, proposal.Proposal.ProposalOnly) || (proposal.ProposedOn != today && firing?.Contains(proposal.Proposal.Rule) != true))
            {
                continue;
            }

            try
            {
                var result = await apply.HandleAsync(new ApplyKillCommand(proposal.Id, KillActors.Auto), cancellationToken);
                if (result.IsSuccess)
                {
                    applied++;
                }
                else
                {
                    failed++;
                    KillLog.StrategyFailed(logger, result.Error.Code);
                }
            }
#pragma warning disable CA1031 // One proposal that cannot be applied must not stop the others; it stays open and is tried again by the next run (the evaluation of the day is recorded, and the same-day retry above applies the proposals of today).
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
            {
                failed++;
                KillLog.StrategyThrew(logger, failure.GetType().Name);
            }
        }

        return (applied, failed);
    }
}
