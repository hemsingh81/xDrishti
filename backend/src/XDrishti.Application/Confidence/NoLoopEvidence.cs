using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The one source of loop evidence in this version, and it says nothing is available (spec CF-12 part 12a, ADR 0218). Nothing is invented: a shadow candidate does not record which model scored it or with what score (CF-08, ADR 0217), the epoch's frozen champion
/// artifact is not kept as a scoring source for old features, and the refit loop that produces the learning-curve gains is not built. Each missing input is a later step; the calculators and the report are already in place.
/// </summary>
internal sealed class NoLoopEvidence : ILoopEvidence
{
    public ValueTask<LoopEvidence> ReadAsync(CancellationToken cancellationToken) =>
        // shortcut: no source for any of the four sections yet (stored scores per candidate, the epoch's frozen artifact, the refit loop, outcomes with stored probabilities).
        ValueTask.FromResult(new LoopEvidence(
            null, "no per-candidate scores are stored: a shadow candidate records neither the model that scored it nor its score, and the epoch's frozen champion artifact is not kept as a scoring source for old features",
            null, "the refit loop that produces the gains of the learning curve is not built",
            null, "no outcome is stored with the probability a model gave before it",
            null, "no shadow candidate stores a score, so no night can be ranked by it"));
}
