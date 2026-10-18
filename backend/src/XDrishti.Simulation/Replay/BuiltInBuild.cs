using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Replay;

/// <summary>What <see cref="BuiltInStrategies.Build"/> returns: the definition and the effective parameters (the defaults filled in, in ordinal name order), or no definition and the located problems.</summary>
/// <param name="Definition">The definition, or null when the id or a parameter name is not known.</param>
/// <param name="Parameters">The effective parameter values; empty when there is no definition.</param>
/// <param name="Problems">The located problems, empty when the build succeeded.</param>
public sealed record BuiltInBuild(StrategyDefinition? Definition, IReadOnlyDictionary<string, double> Parameters, IReadOnlyList<LocatedProblem> Problems);
