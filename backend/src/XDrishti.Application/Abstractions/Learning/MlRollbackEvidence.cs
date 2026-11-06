using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// Where the rollback triggers read a champion's outcomes and cells from (CF-10 part 10d-1, spec D8). Read only. In this version the only implementation answers that nothing is available: a shadow candidate does not record which model scored it (CF-08), so the champion's
/// scored outcomes have no source, and neither have the cells it scores; the daily run therefore finds no trigger. The detectors themselves (P6-09) are real and tested.
/// </summary>
public interface IRollbackEvidence
{
    /// <summary>The champion's scored outcomes and the trade series of the cells it scores; a part that cannot be had is null.</summary>
    ValueTask<RollbackInputs> ReadAsync(StoredModel champion, CancellationToken cancellationToken);
}

/// <summary>What is known about a challenger beyond the registry (CF-10 part 10d-1, spec D7). A null part is not available, and the promotion rule counts that as not a pass.</summary>
/// <param name="GradedCandidates">Graded shadow candidates the challenger scored.</param>
/// <param name="Weeks">Weeks they span.</param>
/// <param name="ProbabilityBeatsChampion">The paired day-cluster bootstrap's probability that the challenger beats the champion on the same candidates (with the take rule of P6-13).</param>
/// <param name="Counterfactual">The P6-12 counterfactual's conclusion for the challenger's recipe.</param>
public sealed record ChallengerFigures(int? GradedCandidates, double? Weeks, double? ProbabilityBeatsChampion, CounterfactualOutcome? Counterfactual)
{
    /// <summary>Nothing is available.</summary>
    public static ChallengerFigures None { get; } = new(null, null, null, null);
}

/// <summary>
/// Where the promotion rule reads a challenger's forward figures from (CF-10 part 10d-1, spec D7). Read only. The only implementation in this version answers that nothing is available, because no source exists: stored challenger scores per shadow candidate (the candidate
/// does not name the model that scored it), and a link from a registered model to the P6-12 counterfactual of its recipe. Promotion is therefore refused with the reason, never passed.
/// </summary>
public interface IChallengerEvidence
{
    /// <summary>The figures for the challenger against the current champion (null when there is none).</summary>
    ValueTask<ChallengerFigures> ReadAsync(StoredModel challenger, StoredModel? champion, CancellationToken cancellationToken);
}
