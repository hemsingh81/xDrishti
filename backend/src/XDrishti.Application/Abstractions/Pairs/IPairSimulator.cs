using XDrishti.Domain.Common;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Application.Abstractions.Pairs;

/// <summary>The pair walker behind a port (P14-13): the application layer cannot reference the simulator; the infrastructure adapter calls <c>PairWalker</c>. Pure calculation; the trades are gross (the charges are <c>PairCosting</c>'s).</summary>
public interface IPairSimulator
{
    /// <summary>The version of the pair walker, <c>pair-1</c>.</summary>
    string WalkerVersion { get; }

    /// <summary>The version of the single-instrument engine whose fill and slippage rules the walker reuses, <c>sim-9</c>.</summary>
    string FillEngineVersion { get; }

    /// <summary>Walks one pair; a failure is a bad input (options, ticks, bars not ascending).</summary>
    Result<PairWalkResult> Walk(PairWalkInput input);
}
