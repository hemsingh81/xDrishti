namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// The only way Planning, Selection and Autonomy may read a model's score for a decision (CF-10 part 10c-2, spec D6 and AC10, ADR 0217); an architecture test forbids them every other route (the scorer ports, the registry, the score query). The gate answers the predicate
/// <c>MlMayInfluence</c> and a score is handed out only when it holds and the model is the current champion; every other path is a refusal with its reasons. The owner's switch is off by default, so the gate refuses everything until the owner turns it on. Nothing here places an order.
/// </summary>
public interface IMlInfluenceGate
{
    /// <summary>May this model influence a decision now? Records the answer (insert-only) when it changed.</summary>
    ValueTask<MlInfluenceAnswer> AnswerAsync(long modelId, CancellationToken cancellationToken);

    /// <summary>The model's P(win) for one feature vector (in the order of the model's feature catalog), or null with the reasons when the gate refuses.</summary>
    ValueTask<MlGatedScore> ScoreAsync(long modelId, IReadOnlyList<double> features, CancellationToken cancellationToken);
}
