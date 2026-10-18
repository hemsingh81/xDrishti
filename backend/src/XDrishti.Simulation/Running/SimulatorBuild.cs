using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Running;

/// <summary>
/// What <see cref="Simulator.Create"/> returns: the simulator, or no simulator and the located problems that stopped it. When a simulator exists, <see cref="Problems"/> holds the warnings of the compile; when it does not, it holds
/// the errors (the problems of the compile unchanged, with their <c>buy.*</c> or <c>sell.*</c> paths, and the <c>simulation.*</c> refusals) and any warnings found on the way. Errors come first.
/// </summary>
/// <param name="Simulator">The simulator, or null when a side failed to compile or asks for something this slice does not run.</param>
/// <param name="Problems">The located problems, errors first.</param>
public sealed record SimulatorBuild(Simulator? Simulator, IReadOnlyList<LocatedProblem> Problems);
