namespace XDrishti.Domain.Learning;

/// <summary>What the caller states about a hypothesis move (spec P6-10).</summary>
/// <param name="ByOwner">The actor is the owner (not the system).</param>
/// <param name="Autonomy">The autonomy level in force.</param>
/// <param name="Outcome">The outcome of the experiment the hypothesis was judged by, when it has one.</param>
/// <param name="OpenCount">How many hypotheses are open now (for a new proposal).</param>
/// <param name="MaxOpen">The limit (doc 16 <c>max_open_hypotheses</c>).</param>
public sealed record HypothesisFacts(bool ByOwner, AutonomyLevel Autonomy, ExperimentOutcome? Outcome = null, int OpenCount = 0, int MaxOpen = HypothesisLifecycle.DefaultMaxOpen);
