using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>The progress port of hosts that run no replay jobs (the worker, the feed): events go nowhere. The API host replaces it with the hub.</summary>
internal sealed class NullExperimentJobProgress : IExperimentJobProgress
{
    public ValueTask ProgressAsync(ExperimentJobProgress progress, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask FinishedAsync(ExperimentJobFinished finished, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
