using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auto;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SettingsCatalog;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Auto;

/// <summary>The AU programme check (spec AU-19).</summary>
public sealed record EvaluateAutoProgrammeCheckQuery : IQuery<Result<AutoProgrammeCheckView>>;

public sealed record AutoProgrammeCheckView(AutoProgrammeReport Report, string Text);

/// <summary>
/// <c>xd auto check</c> (spec AU-19, ADR 0253): reads the stored facts through <see cref="IAutoProgrammeFactsReader"/>, the job tables through the SimAll ports (the training runs only through the one non-evidence reader) and the Essential settings from the
/// catalog, and hands them to the pure <see cref="AutoProgrammeCheck"/>. Read only; no order, no setting, no write. A read that fails is a missing fact (NOT EVALUABLE), never a pass.
/// </summary>
internal sealed class EvaluateAutoProgrammeCheckHandler(
    IAutoProgrammeFactsReader reader,
    ISettingsCatalogSource catalog,
    ITrainingJobProgress jobs,
    ISimAllStore store,
    ITrainingRunReader runs,
    ILogger<EvaluateAutoProgrammeCheckHandler> logger)
    : IQueryHandler<EvaluateAutoProgrammeCheckQuery, Result<AutoProgrammeCheckView>>
{
    /// <summary>The most jobs looked at, and the most run keys asked of the reader at once.</summary>
    // shortcut: newest 50 jobs only, upgrade when job history grows
    internal const int MaxJobs = AutoProgrammeCheck.JobWindow;

    internal const int KeyBatch = 10_000;

    public async ValueTask<Result<AutoProgrammeCheckView>> HandleAsync(EvaluateAutoProgrammeCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var essential = catalog.Read().Count(e => e.Descriptor.Tier == SettingTier.Essential);
        var stored = await reader.ReadAsync(cancellationToken);
        var facts = new AutoProgrammeFacts(stored.RecoveryPasses, stored.MultiDayRecoveries, stored.RemovalAdrPresent, essential, null, null, null, null, stored.SwitchChanges, stored.AutoOnNightsWithSuggestions);
        facts = await WithJobsAsync(facts, cancellationToken);
        var report = AutoProgrammeCheck.Evaluate(facts);
        var met = report.Items.Count(i => i.Verdict == PhaseVerdicts.Met);
        AutoProgrammeCheckLog.Evaluated(logger, report.Verdict, met);
        return Result.Success(new AutoProgrammeCheckView(report, AutoProgrammeCheck.Render(report)));
    }

    private async ValueTask<AutoProgrammeFacts> WithJobsAsync(AutoProgrammeFacts facts, CancellationToken cancellationToken)
    {
        try
        {
            var all = await jobs.ListProgressAsync(0, MaxJobs, cancellationToken);
            var finished = all.Where(j => j.Job.State is SimAllStates.Completed or SimAllStates.CompletedWithFailures).ToList();
            var chunks = 0;
            var breaches = 0;
            foreach (var snapshot in finished)
            {
                var done = await store.ListFinishedAsync(snapshot.Job.Id, cancellationToken);
                chunks += done.RunKeys.Count;
                var seen = new Dictionary<string, TrainingRunHeader>(StringComparer.Ordinal);
                foreach (var batch in done.RunKeys.Chunk(KeyBatch))
                {
                    foreach (var header in await runs.ListByKeysAsync(batch, cancellationToken))
                    {
                        seen[header.RunKey] = header;
                    }
                }

                // the fence: every finished chunk names a training run that ends before the hold-out; a key the reader does not see is a research run or none
                breaches += done.RunKeys.Count(k => !seen.TryGetValue(k, out var h) || h.To >= snapshot.Job.HoldOutStart);
            }

            return facts with { Jobs = all.Count, FinishedJobs = finished.Count, FinishedJobChunks = chunks, FenceBreaches = breaches };
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            AutoProgrammeCheckLog.Unreadable(logger, failure.GetType().Name);
            return facts;
        }
    }
}
