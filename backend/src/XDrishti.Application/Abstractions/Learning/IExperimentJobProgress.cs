namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Client event <c>experimentProgress</c> (spec P8-18 Part B): a job entered a stage or advanced in it; at most four a second.</summary>
public sealed record ExperimentJobProgress(Guid JobId, string State, string Stage, int Done, int Total, string? Message);

/// <summary>Client event <c>experimentFinished</c>: the last event of a job, with its final state.</summary>
public sealed record ExperimentJobFinished(Guid JobId, string State);

/// <summary>Where the progress of a replay job goes (one port: the host's adapter sends it to the SignalR hub; a missed event costs nothing because the job is served by the API as well).</summary>
public interface IExperimentJobProgress
{
    ValueTask ProgressAsync(ExperimentJobProgress progress, CancellationToken cancellationToken);

    ValueTask FinishedAsync(ExperimentJobFinished finished, CancellationToken cancellationToken);
}
