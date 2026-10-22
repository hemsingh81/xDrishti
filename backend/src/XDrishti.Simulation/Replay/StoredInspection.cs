using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Replay;

/// <summary>What <see cref="StoredStrategies.Inspect"/> returns: the description of a text that read and compiled, or the located problems that refuse it.</summary>
public sealed record StoredInspection(StoredDescription? Description, IReadOnlyList<LocatedProblem> Problems);
