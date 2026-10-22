using XDrishti.Application.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>
/// The results of full tests (spec P5-04, ADR 0060): <c>lab.lab_results</c> behind a port. Insert-only for the runtime role; one row per Lab run.
/// </summary>
public interface ILabResultStore
{
    /// <summary>Stores the result of a run; a second append for the same run writes nothing and returns false.</summary>
    ValueTask<bool> AppendAsync(Guid runId, string strategyId, string strategyHash, LabVerdictView verdict, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The result of a run, or null.</summary>
    ValueTask<LabVerdictView?> GetAsync(Guid runId, CancellationToken cancellationToken);
}
