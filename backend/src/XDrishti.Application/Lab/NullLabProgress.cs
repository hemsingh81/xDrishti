using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Application.Lab;

/// <summary>The progress port of hosts that run no Lab runs (the worker, the feed): events go nowhere. The API host replaces it with the hub.</summary>
internal sealed class NullLabProgress : ILabProgress
{
    public ValueTask ProgressAsync(LabRunProgress progress, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask FinishedAsync(LabRunFinished finished, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
