using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Learning;

/// <summary>
/// Evaluates the hypothesis triggers on replayed data only (spec P6-10): cells with enough new trades since a date, and cells with a drift alarm from the P6-09 replay. It returns proposals to reflect; it writes nothing, starts no run and
/// accepts nothing. The weekly schedule is the pipeline's (P7).
/// </summary>
internal sealed class EvaluateHypothesisTriggersHandler(IRunStore runs, IHypothesisLog log, IQueryHandler<ReplayDriftQuery, Result<DriftReport>> drift, ILogger<EvaluateHypothesisTriggersHandler> logger)
    : IQueryHandler<EvaluateHypothesisTriggersQuery, Result<HypothesisTriggersView>>
{
    public async ValueTask<Result<HypothesisTriggersView>> HandleAsync(EvaluateHypothesisTriggersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Sources is null || query.Sources.Count == 0 || query.Drift is null || query.MinNewTrades < 1 || query.MonitorMonths is < 1 or > 120)
        {
            return Result.Failure<HypothesisTriggersView>(LearningErrors.Invalid([new FieldError("request", "Sources, drift options, a minimum of new trades of 1 or more and monitor months of 1 to 120 are required.")]));
        }

        var read = await CellSourceReader.ReadAsync(runs, query.Sources, cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<HypothesisTriggersView>(read.Error);
        }

        var fresh = HypothesisTriggers.NewTrades(read.Value.Trades.Select(t => (t.CellKey, SessionClock.DateOf(t.Trade.ExitEpochSeconds))), query.Since, query.MinNewTrades);
        var replay = await drift.HandleAsync(new ReplayDriftQuery(query.Sources, query.Drift, query.MonitorMonths, null, new CellStatsOptions(), null), cancellationToken);
        if (replay.IsFailure)
        {
            return Result.Failure<HypothesisTriggersView>(replay.Error);
        }

        var triggers = fresh.Concat(HypothesisTriggers.Drift(replay.Value.Proposals)).ToList();
        var open = await log.CountOpenAsync(cancellationToken);
        HypothesisLog.Triggers(logger, triggers.Count, open);
        return Result.Success(new HypothesisTriggersView(
            triggers,
            open,
            HypothesisLifecycle.DefaultMaxOpen,
            [
                "Evaluated on stored runs only (replayed history). A trigger is a reason to think about a hypothesis: nothing was written, no run was started and nothing was accepted.",
                $"The weekly schedule is not built (P7); {open} of {HypothesisLifecycle.DefaultMaxOpen} hypothesis slots are open.",
            ]));
    }
}
