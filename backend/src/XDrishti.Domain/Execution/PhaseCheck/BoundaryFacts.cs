namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The broker allow-list of the data side as the code holds it, against its pin (criterion 10): what is declared, what is pinned and which order, position or fund probes would be allowed.</summary>
public sealed record BoundaryFacts(IReadOnlyList<string> Current, IReadOnlyList<string> Pinned, IReadOnlyList<string> ProbesAllowed);
