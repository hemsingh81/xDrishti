using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Sweeps;

/// <summary>One sweep with its per-start rows and its spread, computed on read from the stored runs (SR-02).</summary>
public sealed record GetReplaySweepQuery(Guid SweepId) : IQuery<Result<ReplaySweepView>>;

/// <summary>Reads the runs of a sweep through the run and equity reads of part C, so a figure has one implementation; the spread is over the completed starts only.</summary>
internal sealed class GetReplaySweepHandler(IReplayReads reads, IQueryHandler<GetReplayRunQuery, Result<ReplayRunView>> runs, IQueryHandler<GetReplayEquityQuery, Result<ReplayEquityView>> equity)
    : IQueryHandler<GetReplaySweepQuery, Result<ReplaySweepView>>
{
    public async ValueTask<Result<ReplaySweepView>> HandleAsync(GetReplaySweepQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await reads.GetSweepAsync(query.SweepId, cancellationToken) is not { } sweep)
        {
            return Result.Failure<ReplaySweepView>(Error.NotFound("replay.sweep_not_found", "No such replay sweep."));
        }

        var stored = await reads.GetSweepRunsAsync(sweep.Id, cancellationToken);
        var rows = new List<ReplaySweepRunView>(stored.Count);
        var points = new List<ReplaySweepPoint>();
        ReplayRunView? first = null;
        foreach (var run in stored.OrderBy(r => r.From))
        {
            var view = await runs.HandleAsync(new GetReplayRunQuery(run.Id), cancellationToken);
            var figures = view.IsSuccess ? view.Value.Figures : null;
            first ??= view.IsSuccess ? view.Value : null;
            bool? beats = null;
            if (run.Complete && figures is not null)
            {
                var curve = await equity.HandleAsync(new GetReplayEquityQuery(run.Id, null, null), cancellationToken);
                if (curve.IsSuccess && curve.Value.Points.Count > 0 && curve.Value.Points[^1].Benchmark is { } bench)
                {
                    beats = curve.Value.Points[^1].Equity > bench;
                }
            }

            rows.Add(new ReplaySweepRunView(run.Id, run.From, run.State, run.SessionsDone, run.SessionsTotal, figures?.FinalEquity ?? run.LastEquity, figures?.Cagr, figures?.MaxDrawdownPct ?? run.LastDrawdown, figures?.Trades, beats, run.FailReason));
            if (run.Complete)
            {
                points.Add(new ReplaySweepPoint(figures?.Cagr, figures?.MaxDrawdownPct ?? run.LastDrawdown, beats));
            }
        }

        var completed = stored.Count(r => r.Complete);
        var state = stored.Any(r => ReplayRunStates.IsActive(r.State)) ? "running"
            : completed == stored.Count ? ReplayRunStates.Completed
            : stored.Any(r => r.State == ReplayRunStates.Cancelled) ? ReplayRunStates.Cancelled
            : ReplayRunStates.Failed;
        var p = stored.Count == 0 ? null : ReplayReadSupport.ParamsOf(stored[0]);
        return Result.Success(new ReplaySweepView(
            first?.Hindsight ?? ReplayReadSupport.Labels(p),
            sweep.Id,
            stored.Count == 0 ? sweep.Starts[^1] : stored[0].To,
            sweep.CreatedAt,
            state,
            completed == stored.Count && stored.Count > 0,
            sweep.Starts.Count,
            completed,
            ReplayReadSupport.CostsLabel(p),
            rows,
            ReplaySweepRules.Spread(points)));
    }
}

/// <summary>Stops a sweep (SR-02, AC 5): the run in flight after its session and every queued run. The ledger rows stay.</summary>
public sealed record CancelReplaySweepCommand(Guid SweepId) : ICommand;

internal sealed class CancelReplaySweepHandler(IReplayStore store, IReplayReads reads, TimeProvider time) : ICommandHandler<CancelReplaySweepCommand, int>
{
    public async ValueTask<Result<int>> HandleAsync(CancelReplaySweepCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await reads.GetSweepAsync(command.SweepId, cancellationToken) is null)
        {
            return Result.Failure<int>(Error.NotFound("replay.sweep_not_found", "No such replay sweep."));
        }

        var touched = await store.CancelSweepAsync(command.SweepId, time.GetUtcNow(), cancellationToken);
        return touched == 0 ? Result.Failure<int>(Error.Conflict("replay.not_active", "The sweep has finished; there is nothing to cancel.")) : Result.Success(touched);
    }
}
