namespace XDrishti.Domain.Research;

/// <summary>What the placebo walk of CF-07 is asked to do: the real signals, the instruments they roll over (in <see cref="PlaceboShapes.RollOrder"/>) and nothing else. Every shape of <see cref="PlaceboShapes.All"/> is walked for every signal.</summary>
/// <param name="Signals">The real signals, in plan order.</param>
/// <param name="RollOrder">The instruments with a signal, in the fixed roll order.</param>
public sealed record PlaceboWalkPlan(IReadOnlyList<PlaceboSignal> Signals, IReadOnlyList<Guid> RollOrder);
