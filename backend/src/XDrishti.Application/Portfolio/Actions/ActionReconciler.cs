using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>
/// Clears the <c>done</c> decisions whose condition no longer holds (ADR 0022), so that the same rule can fire again when it recurs. It is an UPDATE (<c>done</c> → <c>cleared</c>; nothing is ever deleted) of only the rows still as they were read.
/// It runs <b>after every quotes tick and holdings sync</b> (<see cref="ReconcileAsync"/>: evaluates every Portfolio account) and <b>opportunistically inside the Action-center read</b> (<see cref="ClearStaleAsync"/>: the evaluation the read made anyway),
/// because outside market hours no tick runs and a lot edit could otherwise leave a stale <c>done</c>. That is the one write a read makes: idempotent, owner-only, and a failure here is logged and never fails the caller.
/// </summary>
internal sealed class ActionReconciler(ActionEvaluator evaluator, IActionStates states, TimeProvider clock, ILogger<ActionReconciler> logger)
{
    public const string Actor = "system:action-center";

    /// <summary>The Action center evaluated for every active Portfolio account; clears what is stale. Does nothing without a <c>done</c> decision (one small read). Never throws (except for cancellation).</summary>
    public async ValueTask<int> ReconcileAsync(string source, CancellationToken cancellationToken)
    {
        try
        {
            var live = await states.ListLiveAsync(cancellationToken);
            if (!live.Any(s => s.State == ActionDecision.Done))
            {
                return 0;
            }

            var evaluation = await evaluator.EvaluateAsync(null, false, cancellationToken);
            return await ClearAsync(evaluation, source, fullScope: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a side job of a read, a quote tick and a sync: its failure must not fail them, it is logged and the next one tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ActionsLog.ReconcileFailed(logger, source, ex);
            return 0;
        }
    }

    /// <summary>Clears the stale <c>done</c> decisions of an evaluation that was made anyway. Never throws (except for cancellation).</summary>
    public async ValueTask<int> ClearStaleAsync(ActionEvaluation evaluation, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        try
        {
            return await ClearAsync(evaluation, source, fullScope: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // see above
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ActionsLog.ReconcileFailed(logger, source, ex);
            return 0;
        }
    }

    private async ValueTask<int> ClearAsync(ActionEvaluation evaluation, string source, bool fullScope, CancellationToken cancellationToken)
    {
        var stale = evaluation.StaleDone(fullScope);
        if (stale.Count == 0)
        {
            return 0;
        }

        var cleared = await states.ClearDoneAsync(stale, Actor, clock.GetUtcNow(), cancellationToken);
        if (cleared > 0)
        {
            ActionsLog.Cleared(logger, cleared, source);
        }

        return cleared;
    }
}
