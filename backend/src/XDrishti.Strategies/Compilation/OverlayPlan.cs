using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>An overlay exit as compiled: the kind and its parameters by name as numbers (parameter names resolved). Provisional until P3-02b2: only the shape of the parameters is checked.</summary>
public sealed record OverlayPlan(OverlayKind Kind, IReadOnlyDictionary<string, IReadOnlyList<double>> Values);
