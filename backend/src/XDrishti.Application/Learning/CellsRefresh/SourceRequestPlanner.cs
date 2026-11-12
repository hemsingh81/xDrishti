using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning.Jobs;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning.CellsRefresh;

/// <summary>Rebuilds the exact request of a stored run (a seam so the refresh is tested without an engine).</summary>
public interface ISourceRequestPlanner
{
    /// <summary>The request that made the run, or a failure when it cannot be proven (fail closed).</summary>
    ValueTask<Result<SimulationRequest>> PlanAsync(StoredRunHeader header, string recordJson, CancellationToken cancellationToken);
}

/// <summary>By the replay planner of P8-18: it proves, by compiling, that the request gives back the stored definition hash.</summary>
internal sealed class ReplaySourceRequestPlanner(ReplayPlanner planner) : ISourceRequestPlanner
{
    public async ValueTask<Result<SimulationRequest>> PlanAsync(StoredRunHeader header, string recordJson, CancellationToken cancellationToken)
    {
        var plan = await planner.PlanAsync(header, recordJson, cancellationToken);
        return plan.IsFailure ? Result.Failure<SimulationRequest>(plan.Error) : Result.Success(plan.Value.BaselineRequest());
    }
}
