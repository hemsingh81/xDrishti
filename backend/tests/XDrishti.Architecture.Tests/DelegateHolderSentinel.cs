namespace XDrishti.Architecture.Tests;

/// <summary>A delegate type that a reader could be given instead of a handler contract, hiding an edge from the closure scan of <see cref="KillArchitectureTests"/>. The control of the pin on delegate types: a top-level type, so that the scan reaches it through its holder only.</summary>
internal delegate ValueTask<bool> HiddenEdgeDelegate(CancellationToken cancellationToken);

/// <summary>Stands for a type on the automatic path that is handed a delegate built elsewhere.</summary>
internal sealed class DelegateHolderSentinel(HiddenEdgeDelegate hidden)
{
    public ValueTask<bool> Ask(CancellationToken cancellationToken) => hidden(cancellationToken);
}
