using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>An overlay exit as compiled: the kind and its parameters by name as numbers (parameter names resolved), checked against the overlay's table. Provisional until P4's spec (ADR 0036 Amendment 1).</summary>
public sealed record OverlayPlan(OverlayKind Kind, IReadOnlyDictionary<string, IReadOnlyList<double>> Values);
