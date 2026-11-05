namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// Where the figures of the influence predicate come from (CF-10 part 10c-2, ADR 0217): the hold-out scoring, the forward shadow grading and the calibration detector. Read only. In this version the only implementation answers that nothing is available, because none of the three has a source yet.
/// </summary>
public interface IMlInfluenceEvidence
{
    /// <summary>The figures for the model; anything that cannot be had is null.</summary>
    ValueTask<MlInfluenceEvidence> ReadAsync(StoredModel model, CancellationToken cancellationToken);
}
