namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Reads what a stored training set was made from (<c>research.training_sets.sources</c>): <c>sim-all:{job id}</c> for a set made from a simulation job, else <c>runKey:style:exit</c> joined by commas. Read only (CF-10e part 3).</summary>
public interface ITrainingSetSources
{
    /// <summary>The sources text of the set, or null when no such set is stored.</summary>
    ValueTask<string?> SourcesAsync(string setId, CancellationToken cancellationToken);
}
