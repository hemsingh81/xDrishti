using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Running;

/// <summary>What <see cref="SimulatorComparison.Create"/> returns: the comparison, or no comparison and the located problems that stopped it (errors first; the path of a refused candidate starts with <c>candidates[i]</c>).</summary>
/// <param name="Comparison">The comparison, or null.</param>
/// <param name="Problems">The located problems.</param>
public sealed record ComparisonBuild(SimulatorComparison? Comparison, IReadOnlyList<LocatedProblem> Problems);
