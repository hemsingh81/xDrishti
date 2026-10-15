using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Attention;

/// <summary>
/// The writer of the attention items (ADR 0027, ADR 0030): loads the facts of all accounts at once, derives the items (<see cref="AttentionDeriver"/>), remembers them and resolves what no longer holds - three reads and one write however many accounts there are. Runs are
/// one at a time in the process, so a slower reconciliation that worked from older facts can never overwrite a newer one. Also deletes the resolved items past their retention.
/// </summary>
internal sealed class AttentionReconciler(
    IAttentionFactsSource facts,
    IAttentionStore store,
    IHealSettings settings,
    TimeProvider clock,
    ILogger<AttentionReconciler> logger) : IAttentionReconciler
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public async ValueTask ReconcileAsync(CancellationToken cancellationToken)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            var loaded = await facts.LoadAsync(cancellationToken);
            var derived = AttentionDeriver.Derive(loaded, settings)
                .Select(i => new AttentionDraft(i.Id, i.Kind, i.Severity, i.Title, i.Text, AttentionActionsJson.Serialize(i.Actions), i.AccountId))
                .ToList();

            // The store writes one row per id in one statement: a repeated id would fail the whole write ("cannot affect a row a second time") and every later reconcile with it. The last one derived wins.
            var drafts = derived.GroupBy(d => d.Id, StringComparer.Ordinal).Select(g => g.Last()).ToList();
            if (drafts.Count < derived.Count)
            {
                AttentionLog.DuplicateIds(logger, derived.Count - drafts.Count);
            }

            await store.ReconcileAsync(drafts, clock.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // bookkeeping: the sync or the tick that asked must not fail because the items could not be worked out; the next call tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AttentionLog.ReconcileFailed(logger, ex);
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    public async ValueTask ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var days = settings.AttentionRetentionDays;
            var deleted = await store.DeleteResolvedBeforeAsync(clock.GetUtcNow().AddDays(-days), cancellationToken);
            if (deleted > 0)
            {
                AttentionLog.Pruned(logger, deleted, days);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // housekeeping: the rows are deleted at the next tick
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AttentionLog.RetentionFailed(logger, ex);
        }
    }
}
