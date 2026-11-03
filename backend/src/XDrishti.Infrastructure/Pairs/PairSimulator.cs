using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Domain.Common;
using XDrishti.Domain.PairResearch;
using XDrishti.Simulation;
using XDrishti.Simulation.Walking;

namespace XDrishti.Infrastructure.Pairs;

/// <summary>The pair walker behind its port (P14-13): the application layer cannot reference the simulator. Pure calculation; the single-instrument engine is untouched.</summary>
internal sealed class PairSimulator : IPairSimulator
{
    public string WalkerVersion => PairWalker.Version;

    public string FillEngineVersion => SimulationEngine.Version;

    public Result<PairWalkResult> Walk(PairWalkInput input) => PairWalker.Walk(input);
}
