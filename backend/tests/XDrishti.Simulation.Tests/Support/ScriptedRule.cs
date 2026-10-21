using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>A test-only <see cref="IExitRule"/> that asks for what a table says at the bars it names (P4-01): the hand-computed rows with more legs than the ladder rule makes. Like <see cref="TestLadderRule"/> it is reachable from no production code.</summary>
internal sealed class ScriptedRule(IReadOnlyDictionary<int, ExitAction[]> script) : IExitRule
{
    public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
    {
        if (script.TryGetValue(bar, out var asked))
        {
            actions.AddRange(asked);
        }
    }
}
