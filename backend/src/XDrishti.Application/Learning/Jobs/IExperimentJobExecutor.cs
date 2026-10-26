using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>Carries out a claimed replay job. The dispatcher calls it on a scope of its own; it always ends the job in the store (except when the host stops, which the next start marks interrupted).</summary>
public interface IExperimentJobExecutor
{
    Task ExecuteAsync(ExperimentJob job, CancellationToken hostStop);
}
