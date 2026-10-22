using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Carries out one claimed run to a final state (the <see cref="LabRunner"/>); a seam so the dispatcher can be tested without a simulation.</summary>
internal interface ILabRunExecutor
{
    /// <summary>Executes the run. Returns when the run has reached a final state, or - when <paramref name="hostStop"/> is cancelled - leaves it running for the next start to mark interrupted.</summary>
    Task ExecuteAsync(LabRun run, CancellationToken hostStop);
}
