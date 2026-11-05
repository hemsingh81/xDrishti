namespace XDrishti.Application.Abstractions.Confidence;

/// <summary>
/// The only source of the inputs of the loop report (spec CF-12 D4 to D7, part 12a, ADR 0218): the candidates scored by each arm before the open, the learning-curve gains, the model's outcomes with their stored probabilities and the shadow candidates with their scores and
/// the owner's approvals. Read only. Each section is either given or missing with a reason (<c>unavailable</c> is never read as a pass). In this version there is one implementation and it returns that nothing is available, because none of these has a source yet (see
/// <c>NoLoopEvidence</c>); a later part replaces it without touching the calculators.
/// </summary>
public interface ILoopEvidence
{
    /// <summary>Reads what is available now.</summary>
    ValueTask<LoopEvidence> ReadAsync(CancellationToken cancellationToken);
}
