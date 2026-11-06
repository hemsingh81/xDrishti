using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Learning;

/// <summary>
/// The daily rollback evaluation of one champion (spec CF-10 D8 and AC11, part 10d-1, ADR 0220). It reads the champion's scored outcomes and its cells through <see cref="IRollbackEvidence"/> and hands them to <see cref="RollbackPolicy"/>, which runs the real P6-09 detectors, and writes one
/// proposal for every trigger that fires (a trigger that is quiet or unavailable never does; a trigger with an open proposal does not make a second; at most one per model, trigger and day). It records the previous champion (found in the changelog) and whether that model's latest
/// evaluation satisfies the predicate without the switch, as information for the owner: <b>nothing is re-promoted here</b>. It records the evaluation, and only then, when the stored switch <c>ml.auto_rollback</c> is ON, asks to apply the open proposals. With the switch OFF, which is the
/// default and the case when no row exists, it changes nothing but its own proposals and evaluation: the registry is untouched. A failure before the evaluation is recorded leaves nothing recorded for the day, so the next run repeats it. No order is placed.
/// </summary>
internal sealed class ProposeRollbackHandler(
    IModelRegistry registry,
    IRollbackEvidence evidence,
    IRollbackProposalReader reader,
    IRollbackProposalStore store,
    IMlMonitorChecks checks,
    IMlSwitchSettings switches,
    ILearningChangelog changelog,
    MlInfluenceEvaluator evaluator,
    ICommandHandler<ApplyRollbackCommand, ApplyRollbackOutcome> apply,
    MlInfluenceOptions influence,
    TimeProvider clock,
    ILogger<ProposeRollbackHandler> logger) : ICommandHandler<ProposeRollbackCommand, ProposeRollbackOutcome>
{
    private const int OpenLimit = 100;
    private const int MaxText = 900;

    // the P6-09 numbers (window 100, 3 checks, gap 5 pp and 2.5 standard errors; 20-trade means, 3 checks): the defaults of the detectors, not a copy of them
    private static readonly DriftOptions Drift = new();

    private sealed record TriggerReading(string Kind, string State, double? Value, double? Limit, string Text);

    public async ValueTask<Result<ProposeRollbackOutcome>> HandleAsync(ProposeRollbackCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var today = IndiaTime.Today(clock.GetUtcNow());
        if (await checks.ExistsAsync(command.ModelId, today, cancellationToken))
        {
            // the evaluation of today is recorded, but an automatic apply that failed after it was recorded is retried here: the proposals made today are still open and the switch is read again
            var again = await ApplyOpenAutomaticallyAsync(command.ModelId, today, null, cancellationToken);
            return Result.Success(new ProposeRollbackOutcome(command.ModelId, true, false, [], again.Applied, again.Failed));
        }

        var model = await registry.FindAsync(command.ModelId, cancellationToken);
        if (model is null)
        {
            return Result.Failure<ProposeRollbackOutcome>(ModelErrors.NotFound(command.ModelId));
        }

        if (model.Role != ModelRole.Champion)
        {
            return Result.Success(new ProposeRollbackOutcome(command.ModelId, false, true, [], 0));
        }

        var inputs = await evidence.ReadAsync(model, cancellationToken);
        var decision = RollbackPolicy.Evaluate(inputs, Drift);
        var settingsHash = MlInfluenceEvaluator.Hash(Drift.KeyText() + "|" + influence.KeyText());
        var created = new List<long>();
        if (decision.ShouldRollback)
        {
            var open = await reader.ListAsync("proposed", model.Id, OpenLimit, cancellationToken);
            var (previous, qualifies) = await PreviousChampionAsync(model.Id, cancellationToken);
            foreach (var trigger in decision.Firing.Where(t => !open.Any(o => o.Proposal.Trigger == t.Kind)))
            {
                var text = trigger.Text.Length > MaxText ? trigger.Text[..MaxText] : trigger.Text;
                var proposal = new NewRollbackProposal(model.Id, trigger.Kind, trigger.Value, trigger.Limit, text, trigger.Cells, previous, qualifies, settingsHash);
                if (await store.InsertAsync(proposal, today, cancellationToken) is { } id)
                {
                    created.Add(id);
                    MlSafetyLog.RollbackProposed(logger, id, model.Id, trigger.Kind);
                }
            }
        }

        var verdicts = JsonSerializer.Serialize(decision.Triggers.Select(t => new TriggerReading(t.Kind, t.State.ToString(), t.Value, t.Limit, t.Text)));
        await checks.InsertAsync(new NewMlMonitorCheck(model.Id, settingsHash, verdicts), today, cancellationToken);
        var applied = await ApplyOpenAutomaticallyAsync(model.Id, today, decision.Firing.Select(t => t.Kind).ToHashSet(StringComparer.Ordinal), cancellationToken);
        return Result.Success(new ProposeRollbackOutcome(model.Id, false, false, created, applied.Applied, applied.Failed));
    }

    /// <summary>The champion that <paramref name="modelId"/> replaced (from the changelog row that named it champion) and whether that model's latest evaluation satisfies the predicate without the switch. Information only.</summary>
    private async ValueTask<(long? Previous, bool Qualifies)> PreviousChampionAsync(long modelId, CancellationToken cancellationToken)
    {
        var history = await changelog.HistoryAsync(string.Create(CultureInfo.InvariantCulture, $"model:{modelId}"), cancellationToken);
        var named = history.LastOrDefault(h => h.Entry is { Kind: "designation", ToState: "champion" });
        if (named?.Entry.BeforeRef is not { } before || !before.StartsWith("model:", StringComparison.Ordinal) || !long.TryParse(before.AsSpan("model:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var previous) || previous == modelId)
        {
            return (null, false);
        }

        var evaluation = await evaluator.EvaluateAsync(previous, cancellationToken);
        return (previous, evaluation?.Result.HoldsWithoutSwitch == true);
    }

    /// <summary>
    /// With the stored switch ON, applies the open proposals of the model that were made today or whose trigger fires in today's reading (<paramref name="firing"/>; null when the reading was not made). An older open proposal whose trigger no longer fires stays for the owner.
    /// With the switch OFF, or with no row, applies nothing.
    /// </summary>
    private async ValueTask<(int Applied, int Failed)> ApplyOpenAutomaticallyAsync(long modelId, DateOnly today, HashSet<string>? firing, CancellationToken cancellationToken)
    {
        var current = await switches.CurrentAsync(cancellationToken);
        if (!current.AutoRollback.On)
        {
            return (0, 0);
        }

        int applied = 0, failed = 0;
        foreach (var proposal in await reader.ListAsync("proposed", modelId, OpenLimit, cancellationToken))
        {
            if (proposal.ProposedOn != today && firing?.Contains(proposal.Proposal.Trigger) != true)
            {
                continue;
            }

            try
            {
                var result = await apply.HandleAsync(new ApplyRollbackCommand(proposal.Id, MlActors.Auto), cancellationToken);
                if (result.IsSuccess)
                {
                    applied++;
                }
                else
                {
                    failed++;
                    MlSafetyLog.ItemFailed(logger, result.Error.Code);
                }
            }
#pragma warning disable CA1031 // One proposal that cannot be applied must not stop the others; it stays open and is tried again by the next run (the evaluation of the day is recorded, and the same-day retry above applies the proposals of today).
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
            {
                failed++;
                MlSafetyLog.ItemThrew(logger, failure.GetType().Name);
            }
        }

        return (applied, failed);
    }
}
